-- V56: Inline brigade guard — performance indexes for real-time suppression at vote time.
-- VoteBrigadeGuard checks same /24 IP block or same fingerprint-prefix cluster
-- within a 10-minute window on the same post.

-- IP block lookup: fast COUNT by (post_id, voter_ip_block, created_at)
CREATE INDEX IF NOT EXISTS idx_votes_brigade_ip_window
    ON votes (post_id, voter_ip_block, created_at DESC)
    WHERE voter_ip_block IS NOT NULL AND is_quarantined = FALSE;

-- Created-at window scan for fingerprint-prefix join (devices.fingerprint lookup)
CREATE INDEX IF NOT EXISTS idx_votes_brigade_post_time
    ON votes (post_id, created_at DESC)
    WHERE is_quarantined = FALSE;
