-- V51: device_category_affinity for anonymous users + controversial_posts view

-- Anonymous cihaz bazlı kategori ilgi takibi
-- user_category_affinity (V18) giriş yapmış kullanıcılar içindir.
-- Bu tablo, hesap açmadan oy kullanan cihazların affinityini saklar.
CREATE TABLE IF NOT EXISTS device_category_affinity (
    device_id   UUID NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    score       DOUBLE PRECISION NOT NULL DEFAULT 0,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (device_id, category_id)
);

CREATE INDEX IF NOT EXISTS idx_device_category_affinity_device
    ON device_category_affinity(device_id, score DESC);

-- controversial_posts: toplulukun ikiye bölündüğü içerikler
-- Keşfet "İkiye Bölenler" bölümü bu view'ı kullanır.
CREATE OR REPLACE VIEW controversial_posts AS
SELECT
    p.id,
    p.title,
    p.content,
    p.image_url,
    p.category_id,
    p.vote_count_hakli,
    p.vote_count_haksiz,
    p.vote_count_hakli + p.vote_count_haksiz AS total_votes,
    ABS(p.vote_count_hakli - p.vote_count_haksiz)::float
        / NULLIF(p.vote_count_hakli + p.vote_count_haksiz, 0) AS balance_ratio,
    p.trend_score,
    p.distribution_stage,
    p.created_at,
    p.updated_at
FROM posts p
WHERE p.status = 'active'
  AND p.is_unlisted = FALSE
  AND (p.vote_count_hakli + p.vote_count_haksiz) > 40
  AND ABS(p.vote_count_hakli - p.vote_count_haksiz)::float
      / NULLIF(p.vote_count_hakli + p.vote_count_haksiz, 0) < 0.2
  AND (p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6)
ORDER BY p.trend_score DESC;
