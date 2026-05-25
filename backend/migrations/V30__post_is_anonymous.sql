ALTER TABLE posts ADD COLUMN IF NOT EXISTS is_anonymous BOOL NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_posts_user_id_anon
    ON posts(user_id) WHERE is_anonymous = FALSE AND user_id IS NOT NULL;
