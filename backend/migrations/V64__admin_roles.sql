-- Admin rol tablosu: kullanıcılara superadmin / moderatör / analist rolü atanır
-- Kullanıcı başına tek rol (PRIMARY KEY user_id)
CREATE TABLE admin_roles (
    user_id     UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    role        TEXT NOT NULL CHECK (role IN ('superadmin', 'moderator', 'analyst')),
    assigned_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    assigned_by TEXT NOT NULL DEFAULT 'system',
    PRIMARY KEY (user_id)
);

CREATE INDEX idx_admin_roles_role ON admin_roles(role);
