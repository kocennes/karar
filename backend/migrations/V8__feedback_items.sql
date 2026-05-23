CREATE TABLE IF NOT EXISTS feedback_items (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id     UUID REFERENCES devices(id) ON DELETE SET NULL,
    user_id       UUID REFERENCES users(id) ON DELETE SET NULL,
    type          TEXT NOT NULL CHECK (type IN ('bug', 'feedback', 'other')),
    subject       VARCHAR(120) NOT NULL,
    message       VARCHAR(2000) NOT NULL,
    contact_email VARCHAR(120),
    app_version   VARCHAR(200),
    platform      VARCHAR(80),
    status        TEXT NOT NULL DEFAULT 'open',
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_feedback_items_status_created
    ON feedback_items(status, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_feedback_items_user_created
    ON feedback_items(user_id, created_at DESC)
    WHERE user_id IS NOT NULL;
