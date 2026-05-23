ALTER TABLE notifications DROP CONSTRAINT IF EXISTS notifications_type_check;

ALTER TABLE notifications
    ADD CONSTRAINT notifications_type_check
    CHECK (type IN ('comment_on_post', 'verdict_milestone', 'verdict_reminder', 'moderation_result'));
