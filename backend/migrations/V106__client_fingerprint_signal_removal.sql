-- V106: Istemci fingerprint'i kotuye kullanim sinyali olarak kullanilmaz.
--
-- devices.fingerprint istemci tarafindan gonderilir ve resmi istemci her kurulumda
-- platform basina sabit bir deger yollar ('karar-android-dev', 'karar-ios-dev',
-- 'karar-web-dev'). Bu yuzden:
--   1. refresh_trend_scores COUNT(DISTINCT d.fingerprint) ile organik oylari platform
--      basina en fazla 1 tekil oya indiriyordu (bot ise rastgele fingerprint ile tek tek
--      sayiliyordu). Tekil oy artik cihaz kimligi (d.id) ile sayilir; yasakli cihazlar
--      (NOT d.is_banned) ve karantina/bastirilmis oylar yine haric tutulur.
--   2. VoteBrigadeGuard'in fingerprint-prefix kurali her populer postta ayni platformun
--      tum organik oylarini bastiriyordu (posts.vote_count_* yani yayinlanan karar bu
--      oylari saymiyordu). Kural kaldirildi; yalnizca bu kuralla bastirilmis ve ayni
--      ag blogu (/24, IPv6 /64) kuralina da takilmayacak oylar geri acilir, etkilenen
--      postlarin sayaclari yeniden hesaplanir. Karantina bayraklari (trend disi) bilincli
--      olarak korunur: cihaz guven puani kaynakli karantina ile ayirt edilemez.
--   3. Bu kurala ait cozulmemis admin uyarilari kapatilir.

CREATE OR REPLACE FUNCTION refresh_trend_scores(p_ids UUID[])
RETURNS void
LANGUAGE SQL
AS $$
WITH signals AS (
    SELECT
        p.id,
        COUNT(DISTINCT CASE WHEN v.vote_type = 'hakli'  THEN d.id END)::int AS unique_hakli,
        COUNT(DISTINCT CASE WHEN v.vote_type = 'haksiz' THEN d.id END)::int AS unique_haksiz,
        p.comment_count,
        EXTRACT(EPOCH FROM (NOW() - p.created_at)) / 3600.0     AS age_hours,
        COALESCE(pvd.avg_dwell,       0.0)                       AS avg_dwell,
        COALESCE(pvd.total_exposures, 0)::bigint                 AS exposures,
        COUNT(DISTINCT v.voter_region)
            FILTER (WHERE v.voter_region IS NOT NULL)::int       AS distinct_regions,
        COALESCE(p.perspective_toxicity, 0.0)                    AS toxicity,
        COALESCE(rpt.cnt, 0)                                     AS pending_reports,
        COALESCE(qc.cnt,  0)                                     AS quality_comments,
        p.ewma_velocity,
        p.ewma_prev_votes
    FROM posts p
    LEFT JOIN votes v
      ON v.post_id = p.id
     AND v.is_quarantined = FALSE
     AND v.is_suppressed = FALSE
    LEFT JOIN devices d ON d.id = v.device_id AND NOT d.is_banned
    LEFT JOIN (
        SELECT post_id,
               SUM(dwell_seconds_total)::float / NULLIF(SUM(dwell_count), 0) AS avg_dwell,
               SUM(view_count)                                                AS total_exposures
        FROM post_views
        GROUP BY post_id
    ) pvd ON pvd.post_id = p.id
    LEFT JOIN LATERAL (
        SELECT COUNT(*)::int AS cnt
        FROM reports r
        WHERE r.target_type = 'post'
          AND r.target_id = p.id
          AND r.status = 'pending'
    ) rpt ON TRUE
    LEFT JOIN LATERAL (
        SELECT COUNT(*)::int AS cnt
        FROM comments c
        WHERE c.post_id = p.id
          AND c.status = 'active'
          AND char_length(c.content) >= 100
    ) qc ON TRUE
    WHERE p.id = ANY(p_ids)
      AND p.status = 'active'
    GROUP BY p.id, p.comment_count, p.created_at,
             pvd.avg_dwell, pvd.total_exposures,
             p.perspective_toxicity,
             p.ewma_velocity, p.ewma_prev_votes,
             rpt.cnt, qc.cnt
),
ewma_step AS (
    SELECT
        s.id,
        s.unique_hakli + s.unique_haksiz AS total_votes,
        0.3 * GREATEST(0, (s.unique_hakli + s.unique_haksiz) - s.ewma_prev_votes)
            + 0.7 * s.ewma_velocity AS new_ewma,
        s.unique_hakli,
        s.unique_haksiz,
        s.comment_count,
        s.age_hours,
        s.avg_dwell,
        s.exposures,
        s.distinct_regions,
        s.toxicity,
        s.pending_reports,
        s.quality_comments
    FROM signals s
),
eff_votes AS (
    SELECT
        e.*,
        GREATEST(ROUND(e.total_votes * 0.7 + e.new_ewma * 0.3)::int, 0) AS eff_v
    FROM ewma_step e
),
final AS (
    SELECT
        ev.id,
        ev.total_votes,
        ev.new_ewma,
        (
            GREATEST(ev.eff_v, 0)
            * (1.0 + LEAST(
                CASE WHEN ev.exposures > 10
                     THEN ev.eff_v::float / ev.exposures * 2.0
                     ELSE 0.2
                END,
                1.0))
            * CASE WHEN ev.total_votes >= 10
                   THEN 1.0 + (1.0 - ABS(ev.unique_hakli - ev.unique_haksiz)::float
                               / GREATEST(ev.total_votes, 1)) * 0.2
                   ELSE 1.0
              END
            + ev.comment_count * 3.0 + ev.quality_comments * 5.0
        )
        / POWER(GREATEST(ev.age_hours, 0.0) + 2.0, 1.5)
        * (0.8 + LEAST(GREATEST(ev.avg_dwell / 30.0, 0.0), 1.5) * 0.2)
        * CASE WHEN ev.toxicity > 0.4
               THEN POWER(1.0 - LEAST(GREATEST((ev.toxicity - 0.4) / 0.4, 0.0), 1.0), 2)
               ELSE 1.0
          END
        * GREATEST(0.1, 1.0 - ev.pending_reports::float / GREATEST(ev.exposures, 1) * 10.0)
        * CASE WHEN ev.distinct_regions >= 3 THEN 1.0
               ELSE 0.3 + ev.distinct_regions::float / 3.0 * 0.7
          END AS trend_score
    FROM eff_votes ev
)
UPDATE posts p
SET trend_score = f.trend_score,
    ewma_velocity = f.new_ewma,
    ewma_prev_votes = f.total_votes,
    updated_at = NOW()
FROM final f
WHERE p.id = f.id;
$$;

-- Etkilenen postlar, oylar geri acilmadan once toplanir (sayaclar sonra yeniden hesaplanir).
CREATE TEMP TABLE v106_affected_posts ON COMMIT DROP AS
SELECT DISTINCT post_id
FROM votes
WHERE suppression_reason = 'brigade_fingerprint_prefix';

-- Yalnizca fingerprint-prefix kuralinin bastirdigi oylar geri acilir. Ayni post + ayni ag
-- blogunda oyun +-10 dakikasinda 5'ten fazla farkli cihaz varsa (VoteBrigadeGuard'in
-- hala gecerli olan ag blogu kurali bu oyu da bastirirdi) oy bastirilmis kalir.
UPDATE votes v
SET is_suppressed = FALSE,
    suppression_reason = NULL,
    suppressed_at = NULL
WHERE v.suppression_reason = 'brigade_fingerprint_prefix'
  AND NOT (
      v.voter_ip_block IS NOT NULL
      AND (
          SELECT COUNT(DISTINCT v2.device_id)
          FROM votes v2
          WHERE v2.post_id = v.post_id
            AND v2.voter_ip_block = v.voter_ip_block
            AND v2.created_at BETWEEN v.created_at - INTERVAL '10 minutes'
                                  AND v.created_at + INTERVAL '10 minutes'
      ) > 5
  );

-- Yayinlanan karar sayaclari (UpdateVoteCountersReturningAsync ile ayni tanim).
UPDATE posts p
SET vote_count_hakli = (
        SELECT COUNT(*)::int
        FROM votes v
        WHERE v.post_id = p.id
          AND v.vote_type = 'hakli'
          AND v.is_suppressed = FALSE
    ),
    vote_count_haksiz = (
        SELECT COUNT(*)::int
        FROM votes v
        WHERE v.post_id = p.id
          AND v.vote_type = 'haksiz'
          AND v.is_suppressed = FALSE
    ),
    updated_at = NOW()
WHERE p.id IN (SELECT post_id FROM v106_affected_posts);

UPDATE admin_alerts
SET is_resolved = TRUE,
    resolved_by = 'migration:V106',
    resolved_at = NOW()
WHERE type = 'brigade_inline_suppressed'
  AND payload->>'detection_kind' = 'fingerprint_prefix'
  AND is_resolved = FALSE;
