CREATE TABLE IF NOT EXISTS user_strikes (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    admin_email TEXT NOT NULL,
    reason      TEXT NOT NULL,
    severity    TEXT NOT NULL CHECK (severity IN ('light', 'medium', 'heavy')),
    note        TEXT,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_user_strikes_user_id
    ON user_strikes(user_id, created_at DESC);
