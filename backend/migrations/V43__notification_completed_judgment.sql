-- Extend growth_events event_type constraint to include notification attribution event.
-- notification_completed_judgment: fired when a user who opened a push notification
-- subsequently votes on the linked post (completing the judgment loop).
ALTER TABLE growth_events DROP CONSTRAINT IF EXISTS growth_events_event_type_check;

ALTER TABLE growth_events
    ADD CONSTRAINT growth_events_event_type_check
    CHECK (event_type IN (
        'share_landing_opened',
        'share_landing_vote_attempt',
        'share_landing_completed_judgment',
        'share_to_install',
        'notification_completed_judgment'
    ));

CREATE INDEX IF NOT EXISTS idx_growth_events_notification_judgment
    ON growth_events(event_type, created_at DESC)
    WHERE event_type = 'notification_completed_judgment';
