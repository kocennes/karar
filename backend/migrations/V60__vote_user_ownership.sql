ALTER TABLE votes
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_votes_user_id
    ON votes(user_id, created_at DESC)
    WHERE user_id IS NOT NULL;
