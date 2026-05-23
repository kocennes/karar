ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id);

CREATE INDEX IF NOT EXISTS idx_posts_user_id
    ON posts(user_id, created_at DESC)
    WHERE user_id IS NOT NULL AND status != 'deleted';
