-- Bir yanıtın üst yorumu aynı gönderiye ait olmalı: başka bir gönderinin yorum ağacına yanıt
-- enjekte edilip "Yargılanan" (gönderi sahibi) rozeti taklit edilmesini veritabanı seviyesinde de engeller.
-- comments.id zaten PK; bileşik FK'nin hedefi için (post_id, id) benzersizliği gerekir.
ALTER TABLE comments DROP CONSTRAINT IF EXISTS comments_parent_same_post_fk;
ALTER TABLE comments DROP CONSTRAINT IF EXISTS comments_post_id_id_key;

ALTER TABLE comments
    ADD CONSTRAINT comments_post_id_id_key UNIQUE (post_id, id);

-- NOT VALID: yeni ve güncellenen satırlarda zorunludur, mevcut satırlar taranmaz (deploy sırasında
-- eski veriler yüzünden migration başarısız olmaz). Geçmişte enjekte edilmiş yanıtlar okuma tarafında
-- (descendants CTE'de cm.post_id = @postId) zaten gösterilmez. Temizlikten sonra isteğe bağlı olarak:
--   ALTER TABLE comments VALIDATE CONSTRAINT comments_parent_same_post_fk;
-- parent_id NULL olan üst seviye yorumlar MATCH SIMPLE gereği kontrol dışıdır.
ALTER TABLE comments
    ADD CONSTRAINT comments_parent_same_post_fk
    FOREIGN KEY (post_id, parent_id) REFERENCES comments (post_id, id)
    ON DELETE CASCADE
    NOT VALID;
