CREATE TABLE IF NOT EXISTS device_trust_scores (
    device_id UUID PRIMARY KEY REFERENCES devices(id) ON DELETE CASCADE,
    trust_score DOUBLE PRECISION NOT NULL DEFAULT 0.5,
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    failed_integrity_count INTEGER NOT NULL DEFAULT 0,
    report_abuse_count INTEGER NOT NULL DEFAULT 0,
    vote_breadth_count INTEGER NOT NULL DEFAULT 0,
    is_suspicious BOOLEAN NOT NULL DEFAULT FALSE,
    suspicious_reason TEXT,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_device_trust_suspicious
    ON device_trust_scores(is_suspicious, updated_at DESC)
    WHERE is_suspicious = TRUE;

ALTER TABLE votes
    ADD COLUMN IF NOT EXISTS is_quarantined BOOLEAN NOT NULL DEFAULT FALSE;

CREATE INDEX IF NOT EXISTS idx_votes_post_trusted
    ON votes(post_id, vote_type)
    WHERE is_quarantined = FALSE;
