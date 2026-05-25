-- AutoMod kural motoru: keyword/regex filtresi ve davranış kuralları
CREATE TABLE automod_rules (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT NOT NULL,
    rule_type   TEXT NOT NULL CHECK (rule_type IN ('keyword', 'regex', 'behavior')),
    -- keyword/regex için pattern, behavior için JSON config
    pattern     TEXT,
    config      JSONB,
    action      TEXT NOT NULL CHECK (action IN ('hide', 'queue', 'suspend', 'flag')),
    is_active   BOOLEAN NOT NULL DEFAULT TRUE,
    created_by  TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_automod_rules_active ON automod_rules(is_active, rule_type)
    WHERE is_active = TRUE;
