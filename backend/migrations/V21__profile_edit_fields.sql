ALTER TABLE users
    ADD COLUMN IF NOT EXISTS bio TEXT CHECK (bio IS NULL OR char_length(bio) <= 150),
    ADD COLUMN IF NOT EXISTS username_changed_at TIMESTAMPTZ;
