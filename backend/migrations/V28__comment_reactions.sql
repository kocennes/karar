CREATE TABLE IF NOT EXISTS comment_reactions (
    comment_id UUID NOT NULL REFERENCES comments(id) ON DELETE CASCADE,
    device_id  UUID NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    emoji      TEXT NOT NULL CHECK (emoji IN ('👍', '❤️', '😂', '😮', '😢', '😡', '👏', '🔥')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (comment_id, device_id)
);

CREATE INDEX IF NOT EXISTS idx_comment_reactions_comment
    ON comment_reactions(comment_id, emoji);
