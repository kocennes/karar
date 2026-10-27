-- V109: Görsel moderasyonu fail-closed yeniden deneme durumu.
--
-- ImageModerationWorker, SafeSearch sonuç döndürmediğinde (Vision kapalı/kota/anahtar hatası,
-- zaman aşımı, safeSearchAnnotation olmayan yanıt) görseli "varsayılan olarak onaylıyordu" ve
-- yalnızca son 6 saatin en yeni 20 bekleyen görseline bakıyordu. Artık görsel 'pending' (her yerde
-- gizli) kalır ve üstel geri çekilmeyle yeniden denenir; bu kolonlar deneme sayısını ve bir sonraki
-- deneme zamanını tutar.
ALTER TABLE posts
    ADD COLUMN IF NOT EXISTS image_moderation_attempts INTEGER NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS image_moderation_next_attempt_at TIMESTAMPTZ;
