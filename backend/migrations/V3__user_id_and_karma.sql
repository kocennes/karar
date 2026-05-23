-- user_id kolonu: kayıtlı kullanıcıların postları ve yorumları izlenir
-- Nullable: misafir kullanıcılar device_id ile tanımlanır

ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL;

ALTER TABLE comments
    ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL;

-- Karma güncellemeleri için: hangi milestone'lar zaten verildi?
-- Tekrar verilmesini önler (idempotent)
CREATE TABLE IF NOT EXISTS karma_milestones (
    id          UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    source_type TEXT NOT NULL CHECK (source_type IN ('post_vote', 'comment_upvote')),
    source_id   UUID NOT NULL,
    milestone   INTEGER NOT NULL,
    karma_delta INTEGER NOT NULL,
    created_at  TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    UNIQUE (source_type, source_id, milestone)
);

CREATE INDEX IF NOT EXISTS idx_karma_milestones_user ON karma_milestones(user_id);

-- Hızlı kullanıcı araması için post/comment index'leri
CREATE INDEX IF NOT EXISTS idx_posts_user_id ON posts(user_id) WHERE user_id IS NOT NULL;
CREATE INDEX IF NOT EXISTS idx_comments_user_id ON comments(user_id) WHERE user_id IS NOT NULL;
