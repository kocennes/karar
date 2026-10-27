-- V111: Brigade bastırması (post, cihaz) bazında yapışkan.
-- Bastırılmış bir oy DELETE /posts/{id}/vote ile silinip pencere (10 dk) dışında yeniden
-- verildiğinde VoteBrigadeGuard kümeyi tekrar görmüyordu; oy aklanıp sayaçlara dönüyordu.
-- Silme anında bastırma burada saklanır, yeniden oy verildiğinde UpsertVoteAsync geri uygular.
CREATE TABLE IF NOT EXISTS vote_suppression_history (
    post_id            UUID NOT NULL REFERENCES posts(id) ON DELETE CASCADE,
    device_id          UUID NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    suppression_reason TEXT,
    suppressed_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (post_id, device_id)
);

CREATE INDEX IF NOT EXISTS idx_vote_suppression_history_device
    ON vote_suppression_history (device_id);
