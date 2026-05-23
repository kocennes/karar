CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE TABLE devices (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    fingerprint     TEXT NOT NULL,
    platform        TEXT NOT NULL CHECK (platform IN ('android', 'ios', 'web')),
    app_version     TEXT NOT NULL,
    device_token    TEXT NOT NULL UNIQUE,
    is_banned       BOOLEAN NOT NULL DEFAULT FALSE,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_devices_fingerprint ON devices(fingerprint);
CREATE INDEX idx_devices_is_banned ON devices(is_banned) WHERE is_banned = TRUE;

CREATE TABLE fcm_tokens (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id   UUID NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    token       TEXT NOT NULL UNIQUE,
    platform    TEXT NOT NULL CHECK (platform IN ('android', 'ios', 'web')),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_fcm_tokens_device_id ON fcm_tokens(device_id);

CREATE TABLE categories (
    id          INTEGER PRIMARY KEY,
    name        TEXT NOT NULL,
    emoji       TEXT NOT NULL,
    sort_order  INTEGER NOT NULL DEFAULT 0
);

INSERT INTO categories (id, name, emoji, sort_order) VALUES
    (1, 'İş Hayatı', 'İş', 1),
    (2, 'İlişkiler', 'Aşk', 2),
    (3, 'Aile', 'Ev', 3),
    (4, 'Arkadaşlık', 'Dost', 4),
    (5, 'Diğer', '...', 5)
ON CONFLICT (id) DO NOTHING;

CREATE TABLE posts (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id           UUID NOT NULL REFERENCES devices(id),
    category_id         INTEGER NOT NULL REFERENCES categories(id),
    title               TEXT NOT NULL CHECK (char_length(title) BETWEEN 10 AND 120),
    content             TEXT NOT NULL CHECK (char_length(content) BETWEEN 50 AND 1500),
    image_url           TEXT,
    status              TEXT NOT NULL DEFAULT 'active'
        CHECK (status IN ('active', 'under_review', 'auto_hidden', 'deleted')),
    moderation_reason   TEXT,
    moderation_checked_at TIMESTAMPTZ,
    vote_count_hakli    INTEGER NOT NULL DEFAULT 0,
    vote_count_haksiz   INTEGER NOT NULL DEFAULT 0,
    comment_count       INTEGER NOT NULL DEFAULT 0,
    trend_score         DOUBLE PRECISION NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_posts_trend_score ON posts(trend_score DESC) WHERE status = 'active';
CREATE INDEX idx_posts_created_at ON posts(created_at DESC) WHERE status = 'active';
CREATE INDEX idx_posts_device_id ON posts(device_id);
CREATE INDEX idx_posts_category ON posts(category_id, trend_score DESC) WHERE status = 'active';
CREATE INDEX idx_posts_fts ON posts USING GIN(
    to_tsvector('simple', coalesce(title, '') || ' ' || coalesce(content, ''))
);

CREATE TABLE votes (
    post_id     UUID NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    device_id   UUID NOT NULL REFERENCES devices(id),
    vote_type   TEXT NOT NULL CHECK (vote_type IN ('hakli', 'haksiz')),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (post_id, device_id)
);

CREATE INDEX idx_votes_post_id ON votes(post_id);
CREATE INDEX idx_votes_device_id ON votes(device_id);

CREATE TABLE comments (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    post_id             UUID NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    device_id           UUID NOT NULL REFERENCES devices(id),
    content             TEXT NOT NULL CHECK (char_length(content) BETWEEN 5 AND 500),
    status              TEXT NOT NULL DEFAULT 'active'
        CHECK (status IN ('active', 'under_review', 'auto_hidden', 'deleted')),
    moderation_reason   TEXT,
    moderation_checked_at TIMESTAMPTZ,
    upvote_count        INTEGER NOT NULL DEFAULT 0,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_comments_post_id ON comments(post_id, created_at ASC) WHERE status = 'active';
CREATE INDEX idx_comments_device_id ON comments(device_id);

CREATE TABLE comment_upvotes (
    comment_id  UUID NOT NULL REFERENCES comments(id) ON DELETE CASCADE,
    device_id   UUID NOT NULL REFERENCES devices(id),
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (comment_id, device_id)
);

CREATE TABLE reports (
    id                  UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    reporter_device_id  UUID NOT NULL REFERENCES devices(id),
    target_type         TEXT NOT NULL CHECK (target_type IN ('post', 'comment')),
    target_id           UUID NOT NULL,
    reason              TEXT NOT NULL CHECK (reason IN (
        'hate_speech',
        'harassment',
        'personal_info',
        'spam',
        'self_harm',
        'illegal',
        'other'
    )),
    description         TEXT CHECK (description IS NULL OR char_length(description) <= 300),
    status              TEXT NOT NULL DEFAULT 'pending'
        CHECK (status IN ('pending', 'actioned', 'dismissed')),
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (reporter_device_id, target_type, target_id)
);

CREATE INDEX idx_reports_status ON reports(status, created_at ASC) WHERE status = 'pending';
CREATE INDEX idx_reports_target ON reports(target_type, target_id);

CREATE TABLE notifications (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id   UUID NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    type        TEXT NOT NULL CHECK (type IN ('comment_on_post', 'verdict_milestone', 'moderation_result')),
    title       TEXT NOT NULL,
    body        TEXT NOT NULL,
    post_id     UUID REFERENCES posts(id) ON DELETE CASCADE,
    is_read     BOOLEAN NOT NULL DEFAULT FALSE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_notifications_device_id ON notifications(device_id, is_read, created_at DESC);
CREATE INDEX idx_notifications_post_id ON notifications(post_id);

CREATE TABLE bans (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    device_id   UUID NOT NULL REFERENCES devices(id),
    type        TEXT NOT NULL CHECK (type IN ('temporary', 'permanent')),
    reason      TEXT NOT NULL,
    expires_at  TIMESTAMPTZ,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_bans_device_id ON bans(device_id, created_at DESC);
CREATE INDEX idx_bans_expires_at ON bans(expires_at);

CREATE TABLE admin_actions (
    id            UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    admin_email   TEXT NOT NULL,
    action        TEXT NOT NULL,
    target_type   TEXT NOT NULL,
    target_id     UUID,
    note          TEXT,
    created_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_admin_actions_admin_email ON admin_actions(admin_email, created_at DESC);
CREATE INDEX idx_admin_actions_target ON admin_actions(target_type, target_id);
