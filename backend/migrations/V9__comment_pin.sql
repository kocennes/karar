-- Yorum sabitleme: post sahibi bir yorumu en üste sabitleyebilir
ALTER TABLE comments ADD COLUMN is_pinned BOOLEAN NOT NULL DEFAULT FALSE;

-- Bir post için en fazla bir sabitlenmiş yorum olabilir
CREATE UNIQUE INDEX idx_comments_pinned_post
    ON comments (post_id)
    WHERE is_pinned = TRUE;
