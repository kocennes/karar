using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

public sealed class DataRetentionService(
    Db db,
    AffinityService affinity,
    IConfiguration configuration,
    ILogger<DataRetentionService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    private DateTime _lastWeeklyDecay = DateTime.MinValue;

    // Security audit log retention: default 365 days in DB (already archived to GCS by AuditLogExportJob)
    private int AuditLogRetentionDays =>
        configuration.GetValue("AuditLogs:RetentionDays", 365);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DataRetentionService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var anonymizedUsers = await AnonymizeDeletedUsersAsync(stoppingToken);
                if (anonymizedUsers > 0)
                {
                    logger.LogInformation("Anonymized {Count} deleted users", anonymizedUsers);
                }

                var purgedAuditLogs = await PurgeAuditLogsAsync(stoppingToken);
                if (purgedAuditLogs > 0)
                {
                    logger.LogInformation(
                        "Purged {Count} admin_actions records older than {Days} days from DB (archived to GCS)",
                        purgedAuditLogs, AuditLogRetentionDays);
                }

                // Weekly category affinity decay (0.9× every 7 days) — hem user hem device tablosu
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

    private async Task<int> AnonymizeDeletedUsersAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            WITH due_users AS (
                SELECT id, device_id
                FROM users
                WHERE deleted_at IS NOT NULL
                  AND deleted_at < NOW() - INTERVAL '30 days'
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
            SELECT
                (SELECT COUNT(*) FROM anonymized_users) AS user_count,
                (SELECT COUNT(*) FROM anonymized_devices) AS device_count
            """,
            connection
        );

        return Convert.ToInt32(await command.ExecuteScalarAsync(ct));
    }

    // Purge security audit logs (admin_actions) older than retention period from DB.
    // AuditLogExportJob archives these to GCS first; this keeps the DB lean.
    private async Task<int> PurgeAuditLogsAsync(CancellationToken ct)
    {
        var retentionDays = AuditLogRetentionDays;
        if (retentionDays <= 0)
            return 0;

        await using var connection = await db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            DELETE FROM admin_actions
            WHERE created_at < NOW() - (@retentionDays * INTERVAL '1 day')
            """,
            connection);
        command.Parameters.AddWithValue("retentionDays", retentionDays);
        return await command.ExecuteNonQueryAsync(ct);
    }
}
