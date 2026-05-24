ALTER TABLE posts
    ADD COLUMN is_anonymous BOOL NOT NULL DEFAULT FALSE;

CREATE INDEX idx_posts_user_id_anon
    ON posts(user_id)
    WHERE is_anonymous = FALSE AND user_id IS NOT NULL;
