ALTER TABLE banned_subnets
    ADD COLUMN IF NOT EXISTS expires_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_banned_subnets_active
    ON banned_subnets(subnet, expires_at);
