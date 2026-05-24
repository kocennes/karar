ALTER TABLE posts ADD COLUMN IF NOT EXISTS is_featured BOOLEAN NOT NULL DEFAULT FALSE;
ALTER TABLE posts ADD COLUMN IF NOT EXISTS featured_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_posts_featured ON posts(is_featured, featured_at DESC) WHERE is_featured = TRUE;
