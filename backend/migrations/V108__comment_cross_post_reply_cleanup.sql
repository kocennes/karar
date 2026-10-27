-- V108: Daha önce enjekte edilmiş çapraz gönderi yanıtlarını temizle.
--
-- POST /api/v1/posts/{id}/comments, parentId'nin hangi gönderiye ait olduğunu doğrulamıyordu.
-- Saldırgan kendi gönderisi X altında, kurban gönderi Y'deki bir yoruma yanıt oluşturabiliyor;
-- yanıt Y'nin başlığında, X'e göre hesaplanan is_post_owner sayesinde sahte "Yargılanan"
-- rozetiyle görünüyordu. Uygulama katmanı artık bunu reddediyor (INVALID_PARENT) ve
-- V100__comment_parent_same_post.sql veritabanında FOREIGN KEY (post_id, parent_id)
-- REFERENCES comments (post_id, id) ile aynı kuralı yeni/güncellenen satırlar için zorluyor.
-- V100'deki kısıt NOT VALID olduğundan geçmişte enjekte edilmiş satırlar hâlâ duruyor; bu
-- migration onları etkisizleştirir (soft delete, denetim için satır kalır).
--
-- Not: Yalnızca status/moderation_reason/updated_at güncellenir; FK sütunları (post_id, parent_id)
-- değişmediği için NOT VALID kısıt bu UPDATE'te yeniden kontrol edilmez.
UPDATE comments c
SET status = 'deleted',
    moderation_reason = 'Yanıt farklı bir gönderideki yoruma bağlıydı; otomatik kaldırıldı.',
    updated_at = NOW()
FROM comments pc
WHERE c.parent_id = pc.id
  AND c.post_id <> pc.post_id
  AND c.status <> 'deleted';
