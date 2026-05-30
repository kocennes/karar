-- V47: Hot-score algorithm — pure SQL
-- Moves trend score computation (EWMA + geo + safety) from C# to a single UPDATE function.
-- Adds (ewma_velocity, ewma_prev_votes) to posts so Redis is no longer needed for EWMA state.
-- Adds partial indexes sized for p95 < 300ms on the main feed queries.

ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS ewma_velocity   DOUBLE PRECISION NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS ewma_prev_votes INTEGER          NOT NULL DEFAULT 0;

-- refresh_trend_scores(p_ids)
-- Computes, for every supplied post ID in a single pass:
--   1. Unique fingerprint-deduplicated vote counts (quarantined + banned excluded)
--   2. EWMA velocity smoothing (alpha = 0.3) to dampen bot bursts
--   3. Viral bonus, balance bonus, effective comments, dwell multiplier
--   4. Toxicity penalty, report penalty, geo multiplier
-- Then writes trend_score, ewma_velocity, ewma_prev_votes back in one UPDATE.
CREATE OR REPLACE FUNCTION refresh_trend_scores(p_ids UUID[])
RETURNS void
LANGUAGE SQL
AS $$
WITH signals AS (
    SELECT
        p.id,
        COUNT(DISTINCT CASE WHEN v.vote_type = 'hakli'  THEN d.fingerprint END)::int AS unique_hakli,
        COUNT(DISTINCT CASE WHEN v.vote_type = 'haksiz' THEN d.fingerprint END)::int AS unique_haksiz,
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
    LEFT JOIN votes   v ON v.post_id = p.id AND v.is_quarantined = FALSE
    LEFT JOIN devices d ON d.id = v.device_id AND NOT d.is_banned
    LEFT JOIN (
        SELECT post_id,
               SUM(dwell_seconds_total)::float / NULLIF(SUM(dwell_count), 0) AS avg_dwell,
               SUM(view_count)                                                AS total_exposures
        FROM   post_views
        GROUP  BY post_id
    ) pvd ON pvd.post_id = p.id
    LEFT JOIN LATERAL (
        SELECT COUNT(*)::int AS cnt
        FROM   reports r
        WHERE  r.target_type = 'post'
          AND  r.target_id   = p.id
          AND  r.status      = 'pending'
    ) rpt ON TRUE
    LEFT JOIN LATERAL (
        SELECT COUNT(*)::int AS cnt
        FROM   comments c
        WHERE  c.post_id            = p.id
          AND  c.status             = 'active'
          AND  char_length(c.content) >= 100
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
    -- alpha = 0.3; only positive deltas counted (burst dampening)
    SELECT
        s.id,
        s.unique_hakli + s.unique_haksiz                              AS total_votes,
        0.3 * GREATEST(0, (s.unique_hakli + s.unique_haksiz)
                          - s.ewma_prev_votes)
            + 0.7 * s.ewma_velocity                                   AS new_ewma,
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
    -- Effective votes = 70% raw unique + 30% EWMA-smoothed velocity
    SELECT
        e.*,
        GREATEST(
            ROUND(e.total_votes * 0.7 + e.new_ewma * 0.3)::int,
            0
        ) AS eff_v
    FROM ewma_step e
),
final AS (
    SELECT
        ev.id,
        ev.total_votes,
        ev.new_ewma,
        (
            -- engagement = (eff_v × viral_bonus × balance_bonus) + effective_comments
            GREATEST(ev.eff_v, 0)

            -- viral bonus: reward high impression-to-vote conversion
            -- exposures ≤ 10: assume neutral conversion_rate = 0.1 → 0.1×2 = 0.2
            * (1.0 + LEAST(
                CASE WHEN ev.exposures > 10
                     THEN ev.eff_v::float / ev.exposures * 2.0
                     ELSE 0.2
                END,
                1.0))

            -- balance bonus: controversial (≈50/50) posts get up to +20%
            * CASE WHEN ev.total_votes >= 10
                   THEN 1.0 + (1.0 - ABS(ev.unique_hakli - ev.unique_haksiz)::float
                               / GREATEST(ev.total_votes, 1)) * 0.2
                   ELSE 1.0
              END

            -- effective comments: normal×3, quality (≥100 chars)×5
            + ev.comment_count * 3.0 + ev.quality_comments * 5.0
        )

        -- HackerNews gravity: (age_hours + 2)^1.5
        / POWER(GREATEST(ev.age_hours, 0.0) + 2.0, 1.5)

        -- dwell multiplier: clamp(avg_dwell/30, 0, 1.5) contributes 20%
        * (0.8 + LEAST(GREATEST(ev.avg_dwell / 30.0, 0.0), 1.5) * 0.2)

        -- toxicity penalty: quadratic drop above 0.4
        * CASE WHEN ev.toxicity > 0.4
               THEN POWER(1.0 - LEAST(GREATEST(
                        (ev.toxicity - 0.4) / 0.4, 0.0), 1.0), 2)
               ELSE 1.0
          END

        -- report penalty: max(0.1, 1 - pending_reports/exposures × 10)
        * GREATEST(0.1,
            1.0 - ev.pending_reports::float / GREATEST(ev.exposures, 1) * 10.0)

        -- geo multiplier: < 3 distinct regions → proportional penalty
        * CASE WHEN ev.distinct_regions >= 3 THEN 1.0
               ELSE 0.3 + ev.distinct_regions::float / 3.0 * 0.7
          END AS trend_score

    FROM eff_votes ev
)
UPDATE posts p
SET trend_score     = f.trend_score,
    ewma_velocity   = f.new_ewma,
    ewma_prev_votes = f.total_votes,
    updated_at      = NOW()
FROM final f
WHERE p.id = f.id;
$$;

-- ─── Indexes for p95 < 300ms ───────────────────────────────────────────────

-- Main trending feed: active + visible + stage≥3, keyset on (score DESC, id DESC)
CREATE INDEX IF NOT EXISTS idx_posts_feed_trending
    ON posts (trend_score DESC, id DESC)
    WHERE status = 'active' AND is_unlisted = FALSE AND distribution_stage >= 3;

-- Category feed: active + visible + stage≥2, keyset on (category, score DESC, id DESC)
CREATE INDEX IF NOT EXISTS idx_posts_feed_category
    ON posts (category_id, trend_score DESC, id DESC)
    WHERE status = 'active' AND is_unlisted = FALSE AND distribution_stage >= 2;

-- New sort: active + visible, ordered by created_at DESC
CREATE INDEX IF NOT EXISTS idx_posts_feed_new
    ON posts (created_at DESC, id DESC)
    WHERE status = 'active' AND is_unlisted = FALSE;

-- Fresh epsilon-greedy slots: recent posts (<2h), stage≥2
CREATE INDEX IF NOT EXISTS idx_posts_fresh_slots
    ON posts (created_at DESC)
    WHERE status = 'active' AND is_unlisted = FALSE AND distribution_stage >= 2;

-- Stage-1 UCB exploration: only stage=1 posts, ordered by recency
CREATE INDEX IF NOT EXISTS idx_posts_stage1_active
    ON posts (distribution_stage, created_at DESC)
    WHERE status = 'active' AND is_unlisted = FALSE AND distribution_stage = 1;

-- Full-refresh scan: TrendScoreUpdater every-10-min pass (active posts only)
CREATE INDEX IF NOT EXISTS idx_posts_active_score_updated
    ON posts (updated_at DESC)
    WHERE status = 'active';
