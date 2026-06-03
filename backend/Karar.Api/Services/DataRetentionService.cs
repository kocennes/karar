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
        await using var command = new NpgsqlCommand(
            """
            WITH due_users AS (
                SELECT id, device_id
                FROM users
                WHERE deleted_at IS NOT NULL
                  AND deleted_at < NOW() - (@thresholdDays * INTERVAL '1 day')
                  AND email NOT LIKE 'deleted-%@deleted.karar.local'
                LIMIT 500
            ),
            anonymized_users AS (
                UPDATE users u
                SET username = 'silinen_' || substring(replace(u.id::text, '-', '') from 1 for 12),
                    email = 'deleted-' || replace(u.id::text, '-', '') || '@deleted.karar.local',
                    password_hash = NULL,
                    google_id = NULL,
                    ban_reason = NULL,
                    updated_at = NOW()
                FROM due_users d
                WHERE u.id = d.id
                RETURNING d.device_id
            ),
            anonymized_devices AS (
                UPDATE devices d
                SET fingerprint = 'deleted-' || replace(d.id::text, '-', ''),
                    app_version = 'deleted',
                    last_seen_at = NOW()
                FROM anonymized_users u
                WHERE d.id = u.device_id
                RETURNING d.id
            )
            SELECT (SELECT COUNT(*) FROM anonymized_users)
            """,
            connection
        );
        command.Parameters.AddWithValue("thresholdDays", thresholdDays);
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
