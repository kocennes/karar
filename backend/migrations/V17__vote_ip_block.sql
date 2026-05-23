ALTER TABLE votes
    ADD COLUMN IF NOT EXISTS voter_ip_block TEXT;

CREATE INDEX IF NOT EXISTS idx_votes_post_ip_block
    ON votes(post_id, voter_ip_block)
    WHERE voter_ip_block IS NOT NULL;
