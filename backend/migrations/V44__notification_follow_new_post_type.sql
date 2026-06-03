ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_type_check;

ALTER TABLE notifications
    ADD CONSTRAINT notifications_type_check
    CHECK (type IN (
        'comment_on_post',
        'reply_on_comment',
        'verdict_milestone',
        'verdict_reminder',
        'moderation_result',
        'crisis_support',
        'mention',
        'follow',
        'follow_new_post',
        'system_announcement',
        'trend_alert',
        'viral_post_owner',
        'weekly_digest'
    ));
