-- Policy versioning: kullanıcıların kabul ettiği politika sürümlerini takip eder.
-- Büyük politika değişikliklerinde mevcut kullanıcılardan yeniden kabul alınabilmesi için.
-- DEFAULT 1: kayıt sırasında koşulları zaten kabul etmiş mevcut kullanıcıları
-- v1 kabul etmiş sayar, böylece kesinti yaşanmaz.

ALTER TABLE users
  ADD COLUMN IF NOT EXISTS terms_version_accepted   SMALLINT NOT NULL DEFAULT 1,
  ADD COLUMN IF NOT EXISTS privacy_version_accepted SMALLINT NOT NULL DEFAULT 1;
