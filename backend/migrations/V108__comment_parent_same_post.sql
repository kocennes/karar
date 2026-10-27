-- V108: Bir yorum yanıtı yalnızca AYNI gönderideki bir yoruma bağlanabilir.
--
-- POST /api/v1/posts/{id}/comments, parentId'nin hangi gönderiye ait olduğunu doğrulamıyordu.
-- Saldırgan kendi gönderisi X altında, kurban gönderi Y'deki bir yoruma yanıt oluşturabiliyor;
-- yanıt Y'nin başlığında, X'e göre hesaplanan is_post_owner sayesinde sahte "Yargılanan"
-- rozetiyle görünüyordu. Uygulama katmanı artık bunu reddediyor (INVALID_PARENT_COMMENT);
-- bu migration aynı kuralı veritabanında da zorlar.

-- 1) Daha önce enjekte edilmiş çapraz gönderi yanıtlarını etkisizleştir (soft delete, denetim için satır kalır).
UPDATE comments c
SET status = 'deleted',
    moderation_reason = 'Yanıt farklı bir gönderideki yoruma bağlıydı; otomatik kaldırıldı.',
    updated_at = NOW()
FROM comments pc
WHERE c.parent_id = pc.id
  AND c.post_id <> pc.post_id
  AND c.status <> 'deleted';

-- 2) Yeni/güncellenen satırlarda parent yorumun aynı gönderide olmasını zorunlu kıl.
--    (Mevcut satırlar yeniden doğrulanmaz; tetikleyici yalnızca parent_id/post_id yazımında çalışır.)
CREATE OR REPLACE FUNCTION fn_comments_parent_same_post()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    IF NEW.parent_id IS NOT NULL AND NOT EXISTS (
        SELECT 1
        FROM comments pc
        WHERE pc.id = NEW.parent_id
          AND pc.post_id = NEW.post_id
    ) THEN
        RAISE EXCEPTION 'comments.parent_id % does not belong to post %', NEW.parent_id, NEW.post_id
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS trg_comments_parent_same_post ON comments;
CREATE TRIGGER trg_comments_parent_same_post
    BEFORE INSERT OR UPDATE OF parent_id, post_id ON comments
    FOR EACH ROW EXECUTE FUNCTION fn_comments_parent_same_post();
