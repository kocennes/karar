ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS content_source TEXT NOT NULL DEFAULT 'user'
        CHECK (content_source IN ('user', 'system', 'ai'));

COMMENT ON COLUMN posts.content_source IS
    'Origin of the post: user (real user), system (admin/seed), ai (AI-generated). '
    'Non-user content MUST display a visible label in every surface. '
    'See TASK.md B9-9 P0 and docs/moderation.md.';
