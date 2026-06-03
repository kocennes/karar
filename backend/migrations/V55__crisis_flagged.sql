-- Kriz içerik sinyali: intihar/kendine zarar gibi Türkçe pre-filter eşleşmesini işaretler.
-- İçerik cezalandırılmaz; moderasyon kuyruğuna yüksek öncelikle alınır ve
-- içerik sahibine destek bildirimi (182 hattı, imece.org) gönderilir.

ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS crisis_flagged BOOLEAN NOT NULL DEFAULT FALSE;

ALTER TABLE comments
    ADD COLUMN IF NOT EXISTS crisis_flagged BOOLEAN NOT NULL DEFAULT FALSE;

-- Moderasyon kuyruğu için index: kriz bayraklı ve inceleme bekleyen içerikleri hızlı bul.
CREATE INDEX IF NOT EXISTS idx_posts_crisis_flagged
    ON posts (crisis_flagged, status, created_at DESC)
    WHERE crisis_flagged = TRUE;

CREATE INDEX IF NOT EXISTS idx_comments_crisis_flagged
    ON comments (crisis_flagged, status, created_at DESC)
    WHERE crisis_flagged = TRUE;

-- Bildirim tipi kısıtını crisis_support içerecek şekilde güncelle.
ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_type_check;

ALTER TABLE notifications
    ADD CONSTRAINT notifications_type_check
    CHECK (type IN (
        'comment_on_post',
        'reply_on_comment',
        'verdict_milestone',
        'verdict_reminder',
        'moderation_result',
        'mention',
        'follow',
        'follow_new_post',
        'system_announcement',
        'trend_alert',
        'viral_post_owner',
        'weekly_digest',
        'crisis_support'
    ));
