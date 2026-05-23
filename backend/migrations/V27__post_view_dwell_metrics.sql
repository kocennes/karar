ALTER TABLE post_views
    ADD COLUMN IF NOT EXISTS dwell_seconds_total INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS dwell_count INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS interacted_count INT NOT NULL DEFAULT 0;

CREATE INDEX IF NOT EXISTS idx_post_views_post_dwell
    ON post_views(post_id, dwell_count)
    WHERE dwell_count > 0;
