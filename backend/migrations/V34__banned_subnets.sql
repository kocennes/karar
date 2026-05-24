CREATE TABLE IF NOT EXISTS banned_subnets (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    subnet      TEXT NOT NULL UNIQUE,
    reason      TEXT NOT NULL,
    admin_email TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_banned_subnets_subnet ON banned_subnets(subnet);
