-- Görsel moderasyon durumu: NULL = görsel yok, pending = bekliyor, approved = temiz, rejected = engellendi
ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS image_moderation_status TEXT
        CHECK (image_moderation_status IN ('pending', 'approved', 'rejected'));

-- Worker'ın sorgusunu hızlandırmak için partial index
CREATE INDEX IF NOT EXISTS idx_posts_image_moderation_pending
    ON posts (created_at DESC)
    WHERE image_moderation_status = 'pending';
