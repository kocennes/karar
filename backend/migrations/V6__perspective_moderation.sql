-- Perspective API moderation metadata for posts.
-- Used by create-post flow and the admin moderation queue.
ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS perspective_toxicity DOUBLE PRECISION,
    ADD COLUMN IF NOT EXISTS perspective_scores JSONB;

CREATE INDEX IF NOT EXISTS idx_posts_perspective_toxicity
    ON posts (perspective_toxicity DESC)
    WHERE perspective_toxicity IS NOT NULL;
