-- Kullanıcı ↔ cihaz eşlemesi.
-- Engellenen bir kullanıcı JWT'yi düşürüp yalnızca X-Device-Token ile istek atarsa
-- (user_id NULL) blocked_users kontrolleri atlanıyordu. Bu tablo, hesabın kullanıldığı
-- cihazları kaydeder; blok kontrolleri cihaz-only isteklerde buradan hesabı çözer.
-- Kayıt: kayıt cihazı (users.device_id), giriş, Google giriş ve kimlikli yorum.
-- KVKK: hesap silindiğinde (UserDataErasure) bağlantılar silinir; hard delete'te CASCADE.

CREATE TABLE IF NOT EXISTS user_device_links (
    user_id       UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id     UUID NOT NULL REFERENCES devices(id) ON DELETE CASCADE,
    first_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (user_id, device_id)
);

CREATE INDEX IF NOT EXISTS idx_user_device_links_device
    ON user_device_links(device_id);

-- Backfill: kayıt cihazı
INSERT INTO user_device_links (user_id, device_id)
SELECT u.id, u.device_id
FROM users u
WHERE u.deleted_at IS NULL
ON CONFLICT (user_id, device_id) DO NOTHING;

-- Backfill: hesap açıkken kullanılan cihazlar (yorum / oy / post geçmişi)
INSERT INTO user_device_links (user_id, device_id)
SELECT DISTINCT c.user_id, c.device_id
FROM comments c
JOIN users u ON u.id = c.user_id AND u.deleted_at IS NULL
ON CONFLICT (user_id, device_id) DO NOTHING;

INSERT INTO user_device_links (user_id, device_id)
SELECT DISTINCT v.user_id, v.device_id
FROM votes v
JOIN users u ON u.id = v.user_id AND u.deleted_at IS NULL
ON CONFLICT (user_id, device_id) DO NOTHING;

INSERT INTO user_device_links (user_id, device_id)
SELECT DISTINCT p.user_id, p.device_id
FROM posts p
JOIN users u ON u.id = p.user_id AND u.deleted_at IS NULL
ON CONFLICT (user_id, device_id) DO NOTHING;
