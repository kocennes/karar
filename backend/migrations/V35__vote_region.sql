ALTER TABLE votes ADD COLUMN IF NOT EXISTS voter_region TEXT;
CREATE INDEX IF NOT EXISTS idx_votes_post_region ON votes(post_id, voter_region) WHERE voter_region IS NOT NULL;
