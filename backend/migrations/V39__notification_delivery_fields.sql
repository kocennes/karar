ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS payload JSONB NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS priority TEXT NOT NULL DEFAULT 'normal',
    ADD COLUMN IF NOT EXISTS dedupe_key TEXT,
    ADD COLUMN IF NOT EXISTS read_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS dismissed_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS attempt_count INT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS last_error TEXT,
    ADD COLUMN IF NOT EXISTS failed_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS next_attempt_at TIMESTAMPTZ,
    ADD COLUMN IF NOT EXISTS provider_message_id TEXT;

ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_priority_check;
ALTER TABLE notifications
    ADD CONSTRAINT notifications_priority_check
    CHECK (priority IN ('low', 'normal', 'high', 'critical'));

ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_attempt_count_check;
ALTER TABLE notifications
    ADD CONSTRAINT notifications_attempt_count_check
    CHECK (attempt_count >= 0);

UPDATE notifications
SET read_at = created_at
WHERE is_read = TRUE AND read_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_notifications_unsent_next_attempt
    ON notifications (next_attempt_at ASC NULLS FIRST, created_at ASC)
    WHERE sent_at IS NULL AND failed_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_notifications_device_visible
    ON notifications (device_id, is_read, created_at DESC)
    WHERE dismissed_at IS NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_notifications_dedupe_key
    ON notifications (dedupe_key)
    WHERE dedupe_key IS NOT NULL;
