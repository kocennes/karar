-- post_views: tracks which device has seen which post and when.
-- Used for: re-impression limiting (max 2x per 24h), cold-start rescue.
CREATE TABLE IF NOT EXISTS post_views (
    post_id     UUID NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    device_id   UUID NOT NULL,
    view_count  SMALLINT NOT NULL DEFAULT 1,
    first_seen  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (post_id, device_id)
);

-- Used by feed: exclude posts seen >= 2 times by this device in last 24h
CREATE INDEX IF NOT EXISTS idx_post_views_device_recent
    ON post_views(device_id, last_seen DESC)
    WHERE last_seen >= NOW() - INTERVAL '24 hours';

-- Used by cold-start rescue: find posts with 0 views in last 2 hours
CREATE INDEX IF NOT EXISTS idx_post_views_post_count
    ON post_views(post_id);
