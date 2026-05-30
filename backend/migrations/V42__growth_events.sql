CREATE TABLE IF NOT EXISTS growth_events (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    post_id       UUID REFERENCES posts(id) ON DELETE SET NULL,
    device_id     UUID REFERENCES devices(id) ON DELETE SET NULL,
    user_id       UUID REFERENCES users(id) ON DELETE SET NULL,
    event_type    TEXT NOT NULL CHECK (event_type IN (
        'share_landing_opened',
        'share_landing_vote_attempt',
        'share_landing_completed_judgment',
        'share_to_install'
    )),
    source        TEXT,
    platform      TEXT,
    referrer_code TEXT,
    metadata      JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS idx_growth_events_type_created
    ON growth_events(event_type, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_growth_events_post_created
    ON growth_events(post_id, created_at DESC)
    WHERE post_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS idx_growth_events_device_created
    ON growth_events(device_id, created_at DESC)
    WHERE device_id IS NOT NULL;
