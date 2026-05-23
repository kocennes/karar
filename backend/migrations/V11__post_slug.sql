ALTER TABLE posts ADD COLUMN IF NOT EXISTS slug VARCHAR(120);

-- Backfill existing posts with their UUID as slug (new posts will get proper slugs)
UPDATE posts SET slug = id::text WHERE slug IS NULL;

ALTER TABLE posts ALTER COLUMN slug SET NOT NULL;

CREATE UNIQUE INDEX IF NOT EXISTS idx_posts_slug ON posts(slug);
