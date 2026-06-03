-- Reverse lookup index: allows fast check of "is actor blocked by X?"
-- Used by vote/comment endpoints to prevent blocked users from interacting.
CREATE INDEX IF NOT EXISTS idx_blocked_users_blocked_id
    ON blocked_users(blocked_user_id);
