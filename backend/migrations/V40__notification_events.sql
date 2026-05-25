-- Notification analytics event log.
-- Every step of a notification's lifecycle is recorded here:
-- intent → eligible check → suppression decision → send attempt → delivery outcome → user action.

CREATE TABLE notification_events (
    id             UUID         PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id UUID        REFERENCES notifications(id) ON DELETE SET NULL,
    device_id      UUID         NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    event_type     TEXT         NOT NULL,  -- see CHECK below
    occurred_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    metadata       JSONB        NOT NULL DEFAULT '{}'
);

-- Valid lifecycle events:
-- intent          — notification row inserted into outbox
-- eligible        — preference router allowed sending
-- suppressed      — preference router or rate limiter blocked; metadata.reason: muted|quiet_hours|rate_limit|category_disabled|push_disabled
-- send_attempt    — FCM send started
-- sent            — FCM accepted the message; metadata.provider_message_id
-- failed          — FCM rejected permanently or max retries exhausted; metadata.error
-- retrying        — transient failure, scheduled retry; metadata.next_attempt_at
-- opened          — user tapped the push notification
-- dismissed       — user dismissed from notification shade
-- read            — user marked read in in-app notification center
ALTER TABLE notification_events
    ADD CONSTRAINT notification_events_type_check CHECK (event_type IN (
        'intent', 'eligible', 'suppressed',
        'send_attempt', 'sent', 'failed', 'retrying',
        'opened', 'dismissed', 'read'
    ));

-- Query patterns:
--   • "show all events for a notification" → (notification_id, occurred_at)
--   • "device notification history" → (device_id, occurred_at)
--   • "funnel by event type in time window" → (event_type, occurred_at)
CREATE INDEX idx_notification_events_notification ON notification_events (notification_id, occurred_at DESC)
    WHERE notification_id IS NOT NULL;
CREATE INDEX idx_notification_events_device      ON notification_events (device_id, occurred_at DESC);
CREATE INDEX idx_notification_events_type_time   ON notification_events (event_type, occurred_at DESC);
