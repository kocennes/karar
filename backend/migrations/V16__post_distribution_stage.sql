ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS distribution_stage SMALLINT NOT NULL DEFAULT 3;

-- Existing posts are already fully distributed
UPDATE posts SET distribution_stage = 3 WHERE distribution_stage != 3;

CREATE INDEX IF NOT EXISTS idx_posts_stage_1
    ON posts(created_at ASC)
    WHERE distribution_stage < 3 AND status = 'active';
