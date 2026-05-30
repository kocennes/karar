-- Extend growth_events event_type constraint to include feed and search judgment completions.
-- feed_completed_judgment:   verdict_viewed from main feed → post detail flow.
-- search_completed_judgment: verdict_viewed from search results → post detail flow.
ALTER TABLE growth_events DROP CONSTRAINT IF EXISTS growth_events_event_type_check;

ALTER TABLE growth_events
    ADD CONSTRAINT growth_events_event_type_check
    CHECK (event_type IN (
        'share_landing_opened',
        'share_landing_vote_attempt',
        'share_landing_completed_judgment',
        'share_to_install',
        'notification_completed_judgment',
        'feed_completed_judgment',
        'search_completed_judgment'
    ));

CREATE INDEX IF NOT EXISTS idx_growth_events_feed_judgment
    ON growth_events(event_type, created_at DESC)
    WHERE event_type = 'feed_completed_judgment';

CREATE INDEX IF NOT EXISTS idx_growth_events_search_judgment
    ON growth_events(event_type, created_at DESC)
    WHERE event_type = 'search_completed_judgment';
