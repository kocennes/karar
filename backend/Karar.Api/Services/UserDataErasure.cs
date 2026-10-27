using Npgsql;

namespace Karar.Api.Services;

/// KVKK silme (anonimleştirme) rutini. Admin KVKK silme ve DataRetentionService
/// (kullanıcı silmesinden sonraki kurtarma süresi dolunca) aynı rutini kullanır.
///
/// - Doğrudan tanımlayıcılar ve profil verileri silinir; users satırı, içeriklerin
///   (post/yorum) "silinen_xxx" takma adıyla kalabilmesi için anonim olarak tutulur.
/// - users.email NOT NULL + UNIQUE olduğundan NULL yerine benzersiz yer tutucu yazılır.
/// - compliance_logs (5651 sayılı Kanun, append-only, yalnızca hash'li IP/cihaz) yasal
///   saklama yükümlülüğü nedeniyle bu rutinin kapsamı dışındadır.
public static class UserDataErasure
{
    private static readonly string[] Statements =
    [
        // Eski e-posta adresi ile tutulan OTP kayıtları (e-posta üzerine yazılmadan önce)
        "DELETE FROM email_otps WHERE email = (SELECT email FROM users WHERE id = @userId)",
        """
        UPDATE users
        SET username = 'silinen_' || substring(replace(id::text, '-', '') from 1 for 12),
            email = 'deleted-' || replace(id::text, '-', '') || '@deleted.karar.local',
            email_verified = FALSE,
            password_hash = NULL,
            google_id = NULL,
            ban_reason = NULL,
            totp_secret = NULL,
            is_2fa_enabled = FALSE,
            bio = NULL,
            date_of_birth = NULL,
            gender = NULL,
            notification_preferences = '{}'::jsonb,
            deleted_at = COALESCE(deleted_at, NOW()),
            updated_at = NOW()
        WHERE id = @userId
        """,
        """
        UPDATE devices
        SET fingerprint = 'deleted-' || replace(id::text, '-', ''),
            app_version = 'deleted',
            last_seen_at = NOW()
        WHERE id = (SELECT device_id FROM users WHERE id = @userId)
        """,
        "UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = @userId AND revoked_at IS NULL",
        "DELETE FROM totp_backup_codes WHERE user_id = @userId",
        "UPDATE account_recovery_tokens SET used_at = NOW() WHERE user_id = @userId AND used_at IS NULL",
        "DELETE FROM saved_posts WHERE user_id = @userId",
        "DELETE FROM followed_categories WHERE user_id = @userId",
        "DELETE FROM muted_categories WHERE user_id = @userId",
        "DELETE FROM user_category_affinity WHERE user_id = @userId",
        // Kullanıcının kendi engel listesi silinir; başkalarının bu hesaba koyduğu engeller korunur.
        "DELETE FROM blocked_users WHERE blocker_user_id = @userId",
        "DELETE FROM user_device_links WHERE user_id = @userId",
        // IP'den türetilen veriler (oy/rapor) hesaptan koparılır
        "UPDATE votes SET voter_ip_block = NULL, voter_region = NULL WHERE user_id = @userId",
        "UPDATE reports SET reporter_ip_block = NULL WHERE reporter_user_id = @userId",
        "UPDATE feedback_items SET contact_email = NULL, user_id = NULL WHERE user_id = @userId",
        "UPDATE discover_events SET user_id = NULL WHERE user_id = @userId",
        "UPDATE growth_events SET user_id = NULL WHERE user_id = @userId",
    ];

    public static async Task EraseAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid userId,
        CancellationToken ct = default)
    {
        foreach (var sql in Statements)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("userId", userId);
            await command.ExecuteNonQueryAsync(ct);
        }
    }
}
