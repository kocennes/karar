CREATE TABLE IF NOT EXISTS admin_scheduled_reports (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    name        TEXT,
    report      TEXT NOT NULL CHECK (report IN (
        'overview',
        'activity',
        'creation',
        'feed_quality',
        'moderation',
        'notifications',
        'growth',
        'operations'
    )),
    frequency   TEXT NOT NULL CHECK (frequency IN ('daily', 'weekly')),
    format      TEXT NOT NULL CHECK (format IN ('csv', 'json')),
    timezone    TEXT NOT NULL DEFAULT 'Europe/Istanbul',
    filters     JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_by  TEXT NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    is_active   BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE INDEX IF NOT EXISTS idx_admin_scheduled_reports_active
    ON admin_scheduled_reports(is_active, frequency, created_at DESC);
