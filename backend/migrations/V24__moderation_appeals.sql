CREATE TABLE IF NOT EXISTS moderation_appeals (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id      UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    target_type  TEXT NOT NULL CHECK (target_type IN ('post', 'comment')),
    target_id    UUID NOT NULL,
    message      TEXT NOT NULL CHECK (char_length(message) BETWEEN 20 AND 1000),
    status       TEXT NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'approved', 'rejected')),
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reviewed_at  TIMESTAMPTZ,
    reviewed_by  TEXT,
    review_note  TEXT,
    UNIQUE (user_id, target_type, target_id)
);

CREATE INDEX IF NOT EXISTS idx_moderation_appeals_user_id
    ON moderation_appeals(user_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_moderation_appeals_status
    ON moderation_appeals(status, created_at ASC)
    WHERE status = 'pending';

CREATE INDEX IF NOT EXISTS idx_moderation_appeals_target
    ON moderation_appeals(target_type, target_id);
