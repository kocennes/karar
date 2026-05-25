CREATE TABLE IF NOT EXISTS discover_events (
    id               UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    post_id          UUID NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    device_id        UUID REFERENCES devices(id) ON DELETE SET NULL,
    user_id          UUID REFERENCES users(id) ON DELETE SET NULL,
    event_type       TEXT NOT NULL CHECK (event_type IN (
        'impression',
        'dwell',
        'skip',
        'vote',
        'comment_open',
        'comment_reply',
        'comment_like',
        'comment_dislike',
        'save',
        'share',
        'not_interested'
    )),
    dwell_seconds    INT,
    impression_token TEXT,
    metadata         JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at       TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_discover_events_post_created
    ON discover_events(post_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_discover_events_device_created
    ON discover_events(device_id, created_at DESC)
    WHERE device_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_discover_events_type_created
    ON discover_events(event_type, created_at DESC);
