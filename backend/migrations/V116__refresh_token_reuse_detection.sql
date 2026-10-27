-- Refresh token rotasyonunda yeniden kullanım (reuse) tespiti.
-- family_id : aynı girişten türeyen token zinciri. NULL → kök token (ailesi kendi id'si).
-- rotated_at: token /auth/refresh rotasyonuyla iptal edildiyse zamanı. Logout, oturum kapatma,
--             şifre/e-posta değişikliği veya ban ile iptal edilenlerde NULL kalır.
-- Rotasyonla iptal edilmiş bir token kısa eşzamanlılık penceresinden sonra tekrar sunulursa
-- token sızmış kabul edilir ve tüm aile iptal edilir.
ALTER TABLE refresh_tokens
    ADD COLUMN IF NOT EXISTS family_id  UUID,
    ADD COLUMN IF NOT EXISTS rotated_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS idx_refresh_tokens_family_id
    ON refresh_tokens(family_id) WHERE family_id IS NOT NULL;
