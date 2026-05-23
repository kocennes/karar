CREATE TABLE IF NOT EXISTS muted_categories (
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    category_id INTEGER NOT NULL REFERENCES categories(id) ON DELETE CASCADE,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, category_id)
);

CREATE INDEX IF NOT EXISTS idx_muted_categories_user ON muted_categories(user_id);
