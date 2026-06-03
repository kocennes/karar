CREATE TABLE platform_settings (
    key         VARCHAR(100) PRIMARY KEY,
    value       TEXT         NOT NULL,
    updated_at  TIMESTAMPTZ  NOT NULL DEFAULT NOW(),
    updated_by  VARCHAR(255)
);

INSERT INTO platform_settings (key, value) VALUES
    ('audit_log_retention_days',          '365'),
    ('deleted_user_anonymization_days',   '30')
ON CONFLICT (key) DO NOTHING;
