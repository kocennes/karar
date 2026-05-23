-- Track whether an FCM push was sent for each notification.
-- NULL = not yet sent (or no FCM token available).
ALTER TABLE notifications ADD COLUMN IF NOT EXISTS sent_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_notifications_unsent
    ON notifications (created_at ASC)
    WHERE sent_at IS NULL;
