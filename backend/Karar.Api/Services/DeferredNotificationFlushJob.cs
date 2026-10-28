using Npgsql;

namespace Karar.Api.Services;

/// Runs daily at 08:00 Turkey time (UTC+3 = 05:00 UTC).
/// Reads the notif:deferred Redis ZSET populated by NotificationDecisionService when
/// a notification is suppressed due to quiet hours, then resets next_attempt_at = NOW()
/// so NotificationDispatcher picks those notifications up in its next cycle.
public sealed class DeferredNotificationFlushJob(
    IConfiguration configuration,
    NotificationDecisionService decisionService,
    ActivityTracker activityTracker,
    ILogger<DeferredNotificationFlushJob> logger)
    : BackgroundService
{
    // Turkey is UTC+3; 08:00 local = 05:00 UTC
    private static readonly TimeOnly FlushTimeUtc = new(5, 0, 0);

    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("DeferredNotificationFlushJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeUntilNextFlush();
            logger.LogDebug("DeferredNotificationFlushJob sleeping {Delay:hh\\:mm} until next 08:00 flush", delay);

            try
            {
                await Task.Delay(delay, stoppingToken);
                await FlushAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "DeferredNotificationFlushJob flush error");
                // Sleep briefly before retrying to avoid tight error loop
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        var ids = await decisionService.PopDueNotificationsAsync(ct);
        if (ids.Count == 0)
        {
            logger.LogDebug("DeferredNotificationFlushJob: no deferred notifications due");
            return;
        }

        logger.LogInformation("DeferredNotificationFlushJob flushing {Count} deferred notifications", ids.Count);

        // Redis ZSET is the pending-work signal: once ids were popped the DB must be touched even while idle.
        if (activityTracker.IsIdle())
            logger.LogDebug("DeferredNotificationFlushJob: API idle, flushing anyway (Redis signal, daily job)");

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(
            """
            UPDATE notifications
            SET next_attempt_at = NOW(),
                last_error = NULL
            WHERE id = ANY(@ids)
              AND sent_at IS NULL
              AND failed_at IS NULL
            """,
            connection);
        cmd.Parameters.AddWithValue("ids", ids.ToArray());
        var updated = await cmd.ExecuteNonQueryAsync(ct);

        // Unlocked rows are dispatcher work; wake the (idle-gated) NotificationDispatcher.
        if (updated > 0)
            activityTracker.MarkPendingWork();

        logger.LogInformation(
            "DeferredNotificationFlushJob unlocked {Updated}/{Total} deferred notifications",
            updated, ids.Count);
    }

    // ─── Schedule helper ──────────────────────────────────────────────────────

    // Visible for testing
    public static TimeSpan TimeUntilNextFlush(DateTimeOffset? utcNow = null)
    {
        var now = (utcNow ?? DateTimeOffset.UtcNow).UtcDateTime;
        var todayFlush = now.Date.Add(FlushTimeUtc.ToTimeSpan());
        var nextFlush = now < todayFlush ? todayFlush : todayFlush.AddDays(1);
        return nextFlush - now;
    }
}
