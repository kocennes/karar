ALTER TABLE device_trust_scores
    ADD COLUMN IF NOT EXISTS missing_integrity_count INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS invalid_integrity_count INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS expired_integrity_count INTEGER NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS security_events (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_type TEXT NOT NULL,
    device_id UUID REFERENCES devices(id) ON DELETE SET NULL,
    endpoint_key TEXT NOT NULL,
    platform TEXT NOT NULL,
    provider TEXT NOT NULL,
    token_status TEXT NOT NULL CHECK (token_status IN ('valid', 'missing', 'invalid', 'expired', 'skipped')),
    enforce_mode TEXT NOT NULL CHECK (enforce_mode IN ('soft', 'hard')),
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_security_events_type_created
    ON security_events(event_type, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_security_events_attestation_status
    ON security_events(endpoint_key, provider, token_status, created_at DESC)
    WHERE event_type = 'attestation_checked';

CREATE INDEX IF NOT EXISTS idx_security_events_device_created
    ON security_events(device_id, created_at DESC)
    WHERE device_id IS NOT NULL;
