CREATE TABLE IF NOT EXISTS user_category_affinity (
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    score       DOUBLE PRECISION NOT NULL DEFAULT 0,
    updated_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, category_id)
);

CREATE INDEX IF NOT EXISTS idx_user_category_affinity_user
    ON user_category_affinity(user_id, score DESC);
