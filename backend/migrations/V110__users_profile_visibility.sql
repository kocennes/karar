-- V110: GET /api/v1/users/{username}/comments users.is_profile_visible kolonunu okuyor,
-- ancak hiçbir migration bu kolonu oluşturmuyordu (endpoint 500 dönüyordu).
-- Varsayılan TRUE: mevcut davranış (profil herkese açık) korunur.
ALTER TABLE users
    ADD COLUMN IF NOT EXISTS is_profile_visible BOOLEAN NOT NULL DEFAULT TRUE;
