ALTER TABLE comments
    ADD COLUMN IF NOT EXISTS downvote_count INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS comment_downvotes (
    comment_id  UUID NOT NULL REFERENCES comments(id) ON DELETE CASCADE,
    device_id   UUID NOT NULL REFERENCES devices(id),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (comment_id, device_id)
);

CREATE INDEX IF NOT EXISTS idx_comments_wilson
    ON comments(post_id, upvote_count DESC, downvote_count, created_at DESC)
    WHERE status = 'active';
