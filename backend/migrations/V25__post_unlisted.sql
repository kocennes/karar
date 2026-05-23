ALTER TABLE posts ADD COLUMN is_unlisted BOOLEAN NOT NULL DEFAULT FALSE;

-- Indexleri is_unlisted = FALSE olacak şekilde güncelle (feed performansı için)
DROP INDEX idx_posts_trend_score;
CREATE INDEX idx_posts_trend_score ON posts(trend_score DESC) WHERE status = 'active' AND is_unlisted = FALSE;

DROP INDEX idx_posts_created_at;
CREATE INDEX idx_posts_created_at ON posts(created_at DESC) WHERE status = 'active' AND is_unlisted = FALSE;

DROP INDEX idx_posts_category;
CREATE INDEX idx_posts_category ON posts(category_id, trend_score DESC) WHERE status = 'active' AND is_unlisted = FALSE;
