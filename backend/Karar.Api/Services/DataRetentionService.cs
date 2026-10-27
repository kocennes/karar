using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

public sealed class DataRetentionService(
    Db db,
    AffinityService affinity,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private DateTime _lastWeeklyDecay = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DataRetentionService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var auditRetentionDays    = await GetSettingAsync("audit_log_retention_days",          365, stoppingToken);
                var anonymizeDays         = await GetSettingAsync("deleted_user_anonymization_days",    30,  stoppingToken);

                var anonymizedUsers = await AnonymizeDeletedUsersAsync(anonymizeDays, stoppingToken);
                if (anonymizedUsers > 0)
                    logger.LogInformation("Anonymized {Count} deleted users (threshold: {Days}d)", anonymizedUsers, anonymizeDays);

                var purgedAuditLogs = await PurgeAuditLogsAsync(auditRetentionDays, stoppingToken);
                if (purgedAuditLogs > 0)
                    logger.LogInformation("Purged {Count} admin_actions older than {Days}d from DB (archived to GCS)", purgedAuditLogs, auditRetentionDays);

                if (DateTime.UtcNow - _lastWeeklyDecay >= TimeSpan.FromDays(7))
                {
                    await affinity.ApplyWeeklyDecayAsync();
                    await affinity.ApplyWeeklyDeviceDecayAsync();
                    _lastWeeklyDecay = DateTime.UtcNow;
                    logger.LogInformation("Applied weekly affinity decay");
                }

                var purgedRegistrations = await PurgeStaleUnverifiedRegistrationsAsync(stoppingToken);
                if (purgedRegistrations > 0)
                    logger.LogInformation("Purged {Count} unverified registrations older than 24h", purgedRegistrations);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error running data retention cleanup");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task<int> AnonymizeDeletedUsersAsync(int thresholdDays, CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync();

        var dueUserIds = new List<Guid>();
        await using (var select = new NpgsqlCommand(
            """
            SELECT id
            FROM users
            WHERE deleted_at IS NOT NULL
              AND deleted_at < NOW() - (@thresholdDays * INTERVAL '1 day')
              AND email NOT LIKE 'deleted-%@deleted.karar.local'
            LIMIT 500
            """,
            connection))
        {
            select.Parameters.AddWithValue("thresholdDays", thresholdDays);
            await using var reader = await select.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                dueUserIds.Add(reader.GetGuid(0));
            }
        }

        // Admin KVKK silme ile aynı rutin (UserDataErasure); her kullanıcı kendi transaction'ında.
        foreach (var userId in dueUserIds)
        {
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await UserDataErasure.EraseAsync(connection, transaction, userId, ct);
            await transaction.CommitAsync(ct);
        }

        return dueUserIds.Count;
    }

    // Doğrulanmamış şifreli kayıtlar e-posta/kullanıcı adını süresiz rezerve etmesin (başkasının
    // e-postasıyla açılan kayıt sahibin kaydını engeller). Hiç oturum açılamamış bu satırların bağlı
    // verisi olmaz; yine de NO ACTION FK'li tablolarda kayıt varsa satır atlanır.
    private async Task<int> PurgeStaleUnverifiedRegistrationsAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            WITH stale_users AS (
                DELETE FROM users
                WHERE id IN (
                    SELECT u.id
                    FROM users u
                    WHERE u.email_verified = FALSE
                      AND u.auth_provider = 'password'
                      AND u.deleted_at IS NULL
                      AND u.created_at < NOW() - INTERVAL '24 hours'
                      AND NOT EXISTS (SELECT 1 FROM posts p WHERE p.user_id = u.id)
                      AND NOT EXISTS (SELECT 1 FROM account_recovery_tokens r WHERE r.user_id = u.id)
                      AND NOT EXISTS (SELECT 1 FROM admin_roles ar WHERE ar.user_id = u.id)
                    LIMIT 500
                )
                RETURNING email
            ),
            stale_otps AS (
                DELETE FROM email_otps o
                USING stale_users s
                WHERE o.email = s.email
                RETURNING o.id
            )
            SELECT COUNT(*) FROM stale_users
            """,
            connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    // Purge admin_actions older than retention period. AuditLogExportJob archives to GCS first.
    private async Task<int> PurgeAuditLogsAsync(int retentionDays, CancellationToken ct)
    {
        if (retentionDays <= 0) return 0;

        await using var connection = await db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM admin_actions WHERE created_at < NOW() - (@days * INTERVAL '1 day')",
            connection);
        command.Parameters.AddWithValue("days", retentionDays);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<int> GetSettingAsync(string key, int defaultValue, CancellationToken ct)
    {
        try
        {
            await using var connection = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                "SELECT value FROM platform_settings WHERE key = @key", connection);
            cmd.Parameters.AddWithValue("key", key);
            var result = await cmd.ExecuteScalarAsync(ct) as string;
            return result != null && int.TryParse(result, out var val) ? val : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }
}
