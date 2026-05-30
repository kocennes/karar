ALTER TABLE device_trust_scores
    ADD COLUMN IF NOT EXISTS is_quarantined BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_device_trust_quarantined
    ON device_trust_scores(is_quarantined, updated_at DESC)
    WHERE is_quarantined = TRUE;
