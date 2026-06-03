ALTER TABLE comments ADD COLUMN IF NOT EXISTS quality_penalty FLOAT NOT NULL DEFAULT 0;
CREATE INDEX IF NOT EXISTS idx_comments_quality ON comments(quality_penalty)
  WHERE quality_penalty > 0;
