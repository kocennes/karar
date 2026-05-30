-- Dead Letter Queue for notifications that have exhausted all retry attempts
-- or received a permanent delivery failure from FCM.

CREATE TABLE dead_letter_notifications (
    id               UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id  UUID        NOT NULL,
    device_id        UUID        NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    type             TEXT        NOT NULL,
    title            TEXT        NOT NULL,
    body             TEXT        NOT NULL,
    post_id          UUID,
    attempt_count    INT         NOT NULL,
    last_error       TEXT,
    reason           TEXT,
    moved_at         TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_dlq_device ON dead_letter_notifications (device_id, moved_at DESC);
CREATE INDEX idx_dlq_notification ON dead_letter_notifications (notification_id);

-- Outbox status — mirrors sent_at / failed_at but queryable without NULL checks.
ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS status TEXT NOT NULL DEFAULT 'pending';

ALTER TABLE notifications
    ADD CONSTRAINT notifications_status_check
    CHECK (status IN ('pending', 'sent', 'permanently_failed'));

-- Backfill from existing state columns so the new column is consistent.
UPDATE notifications SET status = 'sent'              WHERE sent_at  IS NOT NULL AND status = 'pending';
UPDATE notifications SET status = 'permanently_failed' WHERE failed_at IS NOT NULL AND status = 'pending';
