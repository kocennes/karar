-- 5651 sayılı Kanun gereği yüzey sağlayıcı bağlantı kayıtları.
-- Saklama süresi: 2 yıl.
-- IP ve device bilgileri günlük-tuzlu SHA-256 hash olarak saklanır (raw IP saklanmaz).
-- Uygulama servis hesabı için UPDATE/DELETE yetkisi kaldırılmalıdır (bkz. notlar).

CREATE TABLE IF NOT EXISTS compliance_logs (
    id           BIGSERIAL    PRIMARY KEY,
    logged_at    TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    action       TEXT         NOT NULL,   -- 'vote', 'post_create', 'comment_create', 'login', 'register', 'report'
    ip_hash      TEXT         NOT NULL,   -- SHA256(IP + ":" + yyyyMMdd + ":" + Compliance:DailySalt)
    device_hash  TEXT,                    -- SHA256(device_id + ":" + yyyyMMdd + ":" + Compliance:DailySalt)
    user_id      UUID,
    target_id    UUID,
    target_type  TEXT,                    -- 'post', 'comment', 'user', 'auth'
    metadata     JSONB
);

CREATE INDEX IF NOT EXISTS idx_compliance_logs_logged_at
    ON compliance_logs(logged_at DESC);

CREATE INDEX IF NOT EXISTS idx_compliance_logs_action_date
    ON compliance_logs(action, logged_at DESC);

-- Append-only koruma: uygulama kodu yanlışlıkla UPDATE/DELETE çalıştırmasın.
CREATE OR REPLACE FUNCTION fn_compliance_logs_readonly()
RETURNS TRIGGER LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'compliance_logs tablosu append-only''dır (5651 gereği).';
END;
$$;

DROP TRIGGER IF EXISTS trg_compliance_logs_no_update ON compliance_logs;
CREATE TRIGGER trg_compliance_logs_no_update
    BEFORE UPDATE ON compliance_logs
    FOR EACH ROW EXECUTE FUNCTION fn_compliance_logs_readonly();

DROP TRIGGER IF EXISTS trg_compliance_logs_no_delete ON compliance_logs;
CREATE TRIGGER trg_compliance_logs_no_delete
    BEFORE DELETE ON compliance_logs
    FOR EACH ROW EXECUTE FUNCTION fn_compliance_logs_readonly();

-- NOT: Production'da app servis hesabının UPDATE/DELETE yetkisi kaldırılmalıdır:
--   REVOKE UPDATE, DELETE ON compliance_logs FROM karar_app;
-- Bu migration bu yetkiyi kaldırmaz; DB bootstrap runbook'unda yapılmalıdır.
