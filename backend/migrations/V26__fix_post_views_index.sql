-- Fix: V19__post_views.sql created a partial index with NOW() in the predicate
-- which is invalid in PostgreSQL (predicate must be immutable).
-- Replace with a plain index; the 24h filter is applied at query time.
DROP INDEX IF EXISTS idx_post_views_device_recent;

CREATE INDEX IF NOT EXISTS idx_post_views_device_last_seen
    ON post_views(device_id, last_seen DESC);
