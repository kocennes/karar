-- 2FA yedek kodları: 8 adet tek kullanımlık giriş kodu
CREATE TABLE totp_backup_codes (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    code_hash  TEXT NOT NULL,
    used_at    TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_backup_codes_user_id ON totp_backup_codes(user_id);

-- Hesap kurtarma tokenları: hesap silindikten 30 gün içinde geri alım
CREATE TABLE account_recovery_tokens (
    id         UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id    UUID NOT NULL REFERENCES users(id),
    token_hash TEXT NOT NULL UNIQUE,
    expires_at TIMESTAMPTZ NOT NULL,
    used_at    TIMESTAMPTZ,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
CREATE INDEX idx_account_recovery_tokens_user_id ON account_recovery_tokens(user_id);
