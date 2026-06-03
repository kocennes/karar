-- North-Star: judgment loop event storage.
-- Receives POST /api/v1/analytics/loop-completed from Flutter clients.
-- Enables backend-side funnel aggregation independent of Firebase export.

CREATE TABLE judgment_loop_events (
    id                   BIGSERIAL PRIMARY KEY,
    device_id            TEXT,
    post_id              TEXT,
    source               TEXT,
    loop_duration_seconds INT,
    dwell_seconds        INT,
    created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_judgment_loop_events_created_at
    ON judgment_loop_events(created_at DESC);

CREATE INDEX idx_judgment_loop_events_source
    ON judgment_loop_events(source, created_at DESC);
