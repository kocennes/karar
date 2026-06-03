using Npgsql;
using StackExchange.Redis;

namespace Karar.Api.Services;

/// Central decision engine for all push-notification eligibility checks.
/// Called by NotificationDispatcher once per notification before FCM delivery.
///
/// Decision order:
///   1. Critical priority → always allow (no suppression possible)
///   2. User preference (pushEnabled + category) → suppress or allow
///   3. Quiet hours → add to Redis deferred ZSET (notif:deferred) for 08:00 flush
///   4. Sliding-window rate limit (15 min / 3 pushes) → suppress if exceeded
///   5. All checks passed → allow
///
/// Side effects (always executed regardless of decision):
///   • Logs notification_events: 'intent' on first evaluation, 'eligible' or 'suppressed' per decision
///   • Publishes SSE badge-update event once per notification so in-app badge
///     stays accurate even when pushEnabled = false
public sealed class NotificationDecisionService(
    NotificationPreferenceRouter preferenceRouter,
    NotificationRateLimiter rateLimiter,
    RedisService redis,
    ILogger<NotificationDecisionService> logger)
{
    // ZSET key for quiet-hours deferred notifications; score = UTC unix timestamp of intended delivery
    private const string DeferredZSetKey = "notif:deferred";

    // Prevents duplicate SSE badge events for the same notification across dispatcher cycles
    private static string SseSentKey(Guid notificationId) => $"notif:sse_sent:{notificationId:N}";

    /// Evaluate whether a notification should be pushed, deferred, or suppressed.
    /// Always logs intent + outcome analytics events and publishes the SSE badge event.
    public async Task<NotificationDecision> EvaluateAsync(
        Guid notificationId,
        Guid deviceId,
        DateTimeOffset deviceCreatedAt,
        Guid? userId,
        string type,
        NotificationPriority priority,
        Guid? postId,
        NpgsqlConnection connection,
        CancellationToken ct = default)
    {
        // SSE badge update: fire once per notification so in-app badge is accurate regardless of push state
        await PublishSseBadgeOnceAsync(notificationId, deviceId, userId, connection, ct);

        // Log intent (first time this notification is evaluated)
        await LogEventAsync(connection, notificationId, deviceId, "intent",
            $"{{\"type\":\"{type}\",\"priority\":\"{priority.ToString().ToLowerInvariant()}\"}}",
            ct);

        // 1. Critical notifications bypass all checks
        if (priority == NotificationPriority.Critical)
        {
            await LogEventAsync(connection, notificationId, deviceId, "eligible",
                "{\"reason\":\"critical_priority\"}", ct);
            return NotificationDecision.Allow();
        }

        // 2. Preference check (pushEnabled + category + mute + quiet hours)
        var preferenceDecision = await preferenceRouter.CanPushAsync(
            deviceId, type, priority, connection, ct);

        if (!preferenceDecision.Allowed)
        {
            var reason = preferenceDecision.Reason;

            if (preferenceDecision.IsDeferred && preferenceDecision.SuggestedRetryDelay.HasValue)
            {
                await AddToDeferredZSetAsync(notificationId, preferenceDecision.SuggestedRetryDelay.Value, ct);
            }

            await LogEventAsync(connection, notificationId, deviceId, "suppressed",
                $"{{\"reason\":\"{reason}\"}}",
                ct);
            return preferenceDecision.IsDeferred
                ? NotificationDecision.Defer(reason, preferenceDecision.SuggestedRetryDelay)
                : NotificationDecision.Suppress(reason);
        }

        // 3. Daily/hourly frequency cap (new devices get a stricter daily cap)
        var withinFrequencyCap = await rateLimiter.CanSendAsync(deviceId, priority, deviceCreatedAt, ct);
        if (!withinFrequencyCap)
        {
            await LogEventAsync(connection, notificationId, deviceId, "suppressed",
                "{\"reason\":\"frequency_cap\"}", ct);
            return NotificationDecision.Suppress("frequency_cap");
        }

        // 4. 15-min sliding-window rate limit (max 3 pushes per device per 15 min)
        var withinSlidingWindow = await rateLimiter.CheckSlidingWindowAsync(deviceId, ct);
        if (!withinSlidingWindow)
        {
            await LogEventAsync(connection, notificationId, deviceId, "suppressed",
                "{\"reason\":\"rate_limit\"}", ct);
            return NotificationDecision.Suppress("rate_limit");
        }

        // 5. Per-post fatigue guard: max 1 push per post per hour
        if (postId.HasValue)
        {
            var withinPostCooldown = await rateLimiter.CheckPostCooldownAsync(deviceId, postId.Value, ct);
            if (!withinPostCooldown)
            {
                await LogEventAsync(connection, notificationId, deviceId, "suppressed",
                    $"{{\"reason\":\"post_cooldown\",\"post_id\":\"{postId}\"}}", ct);
                return NotificationDecision.Suppress("post_cooldown");
            }
        }

        // 6. Per-category fatigue guard: max 2 pushes per category per 30 min
        var withinCategoryCooldown = await rateLimiter.CheckCategoryCooldownAsync(deviceId, type, ct);
        if (!withinCategoryCooldown)
        {
            await LogEventAsync(connection, notificationId, deviceId, "suppressed",
                $"{{\"reason\":\"category_cooldown\",\"category\":\"{NotificationRateLimiter.GetNotificationCategory(type)}\"}}", ct);
            return NotificationDecision.Suppress("category_cooldown");
        }

        await LogEventAsync(connection, notificationId, deviceId, "eligible", "{}", ct);
        return NotificationDecision.Allow();
    }

    // ─── Deferred ZSET ──────────────────────────────────────────────────────────

    private async Task AddToDeferredZSetAsync(Guid notificationId, TimeSpan delay, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            var deliverAt = DateTimeOffset.UtcNow.Add(delay).ToUnixTimeSeconds();
            await redis.GetDb().SortedSetAddAsync(
                DeferredZSetKey,
                notificationId.ToString("N"),
                deliverAt);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to add notification {Id} to deferred ZSET", notificationId);
        }
    }

    /// Called by DeferredNotificationFlushJob at 08:00 to retrieve all due notifications.
    /// Returns notification IDs whose deliver_at score has passed.
    public async Task<IReadOnlyList<Guid>> PopDueNotificationsAsync(CancellationToken ct = default)
    {
        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var db = redis.GetDb();

            // Atomically get and remove entries with score <= now
            var entries = await db.SortedSetRangeByScoreAsync(DeferredZSetKey, 0, now);
            if (entries.Length == 0) return Array.Empty<Guid>();

            await db.SortedSetRemoveRangeByScoreAsync(DeferredZSetKey, 0, now);

            var ids = new List<Guid>(entries.Length);
            foreach (var entry in entries)
            {
                if (Guid.TryParseExact(entry!, "N", out var id))
                    ids.Add(id);
            }
            return ids;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to pop due deferred notifications");
            return Array.Empty<Guid>();
        }
    }

    // ─── SSE badge update ────────────────────────────────────────────────────────

    private async Task PublishSseBadgeOnceAsync(
        Guid notificationId,
        Guid deviceId,
        Guid? userId,
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        if (userId is null) return;

        var sseSentKey = SseSentKey(notificationId);
        var db = redis.GetDb();

        try
        {
            // Only publish once per notification lifetime
            var isFirst = await db.StringSetAsync(sseSentKey, "1", TimeSpan.FromHours(24), When.NotExists);
            if (!isFirst) return;

            var unreadCount = await GetUnreadCountAsync(connection, deviceId, ct);
            await redis.PublishUserEventAsync(userId.Value, new
            {
                type = "notification.created",
                notificationId,
                unreadCount,
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "SSE badge publish failed for notification {Id}", notificationId);
        }
    }

    private static async Task<int> GetUnreadCountAsync(
        NpgsqlConnection connection, Guid deviceId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM notifications WHERE device_id = @deviceId AND is_read = FALSE AND dismissed_at IS NULL",
            connection);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    // ─── Event logging ───────────────────────────────────────────────────────────

    private async Task LogEventAsync(
        NpgsqlConnection connection,
        Guid notificationId,
        Guid deviceId,
        string eventType,
        string? metadataJson = null,
        CancellationToken ct = default)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO notification_events (notification_id, device_id, event_type, metadata)
                VALUES (@notificationId, @deviceId, @eventType, @metadata::jsonb)
                """,
                connection);
            cmd.Parameters.AddWithValue("notificationId", notificationId);
            cmd.Parameters.AddWithValue("deviceId", deviceId);
            cmd.Parameters.AddWithValue("eventType", eventType);
            cmd.Parameters.AddWithValue("metadata", metadataJson ?? "{}");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Failed to log {EventType} for notification {Id}", eventType, notificationId);
        }
    }
}

// ─── Decision result ─────────────────────────────────────────────────────────────

public enum NotificationDecisionKind { Allow, Suppress, Defer }

public sealed record NotificationDecision(
    NotificationDecisionKind Kind,
    string? SuppressionReason,
    TimeSpan? DeferDelay)
{
    public bool ShouldSend => Kind == NotificationDecisionKind.Allow;
    public bool IsDeferred => Kind == NotificationDecisionKind.Defer;

    public static NotificationDecision Allow() =>
        new(NotificationDecisionKind.Allow, null, null);

    public static NotificationDecision Suppress(string reason) =>
        new(NotificationDecisionKind.Suppress, reason, null);

    public static NotificationDecision Defer(TimeSpan? delay) =>
        Defer("quiet_hours", delay);

    public static NotificationDecision Defer(string reason, TimeSpan? delay) =>
        new(NotificationDecisionKind.Defer, reason, delay);
}
