-- Brigade coordinated detection, trust score history, enforcement ladder

-- Admin alerts (brigade_suspected, new_account_high_report, etc.)
CREATE TABLE IF NOT EXISTS admin_alerts (
    id           BIGSERIAL PRIMARY KEY,
    type         TEXT NOT NULL,
    payload      JSONB NOT NULL DEFAULT '{}'::jsonb,
    is_resolved  BOOLEAN NOT NULL DEFAULT FALSE,
    resolved_by  TEXT,
    resolved_at  TIMESTAMPTZ,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_admin_alerts_type_unresolved
    ON admin_alerts(type, created_at DESC)
    WHERE is_resolved = FALSE;

CREATE INDEX IF NOT EXISTS idx_admin_alerts_created
    ON admin_alerts(created_at DESC);

-- Device trust score history (last 90 days retention, INSERT on every score change)
CREATE TABLE IF NOT EXISTS device_trust_score_history (
    id          BIGSERIAL PRIMARY KEY,
    device_id   TEXT NOT NULL,
    score       FLOAT NOT NULL,
    reason      TEXT,
    recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_device_trust_score_history_device_recorded
    ON device_trust_score_history(device_id, recorded_at DESC);

-- Enforcement actions (warning -> strike -> temp_ban -> perm_ban)
CREATE TABLE IF NOT EXISTS enforcement_actions (
    id                   BIGSERIAL PRIMARY KEY,
    target_type          TEXT NOT NULL CHECK (target_type IN ('device', 'user')),
    target_id            TEXT NOT NULL,
    action               TEXT NOT NULL CHECK (action IN ('warning', 'strike', 'temp_ban', 'perm_ban')),
    reason               TEXT,
    expires_at           TIMESTAMPTZ,
    created_by_admin_id  TEXT,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_enforcement_actions_target
    ON enforcement_actions(target_type, target_id, created_at DESC);

-- Strike count on devices (incremented by enforcement ladder)
ALTER TABLE devices
    ADD COLUMN IF NOT EXISTS strike_count INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS flags        JSONB NOT NULL DEFAULT '{}'::jsonb;

-- votes.quarantined column (may already exist as is_quarantined, alias for brigade detector)
ALTER TABLE votes
    ADD COLUMN IF NOT EXISTS quarantined BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_votes_quarantined_post
    ON votes(post_id, created_at DESC)
    WHERE quarantined = TRUE;
