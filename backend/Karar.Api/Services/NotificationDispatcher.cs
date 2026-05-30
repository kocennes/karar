using FirebaseAdmin.Messaging;
using Karar.Api.Observability;
using Npgsql;

namespace Karar.Api.Services;

/// DB outbox pattern: every 30 s picks up due notifications, sends FCM push,
/// then stamps sent_at only after the provider accepts the message.
public sealed class NotificationDispatcher(
    IConfiguration configuration,
    ILogger<NotificationDispatcher> logger,
    IFcmSender fcmSender,
    NotificationRateLimiter rateLimiter,
    NotificationPreferenceRouter preferenceRouter,
    SloMetrics sloMetrics,
    RedisService redis)
    : BackgroundService
{
    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 5;

    // ─── Public helper methods (unit-testable) ───────────────────────────────

    // Base delay without jitter — deterministic, used in tests.
    public static TimeSpan CalculateBaseDelay(int attemptCount)
    {
        var seconds = Math.Min(30.0 * Math.Pow(2, attemptCount), 3600.0);
        return TimeSpan.FromSeconds(seconds);
    }

    // Base delay + up to 10% random jitter as specified.
    public static TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var baseDelay = CalculateBaseDelay(attemptCount);
        var jitter = TimeSpan.FromSeconds(Random.Shared.NextDouble() * baseDelay.TotalSeconds * 0.1);
        return baseDelay + jitter;
    }

    /// Pure decision function — determines what DB update is needed after a send attempt.
    /// sent_at is set ONLY when this returns DeliveryAction.MarkSent.
    public static DeliveryDecision DetermineDeliveryDecision(
        PushSendResult result, int attemptCount, int maxAttempts) =>
        result.Status switch
        {
            PushSendStatus.Success =>
                new(DeliveryAction.MarkSent, result.ProviderMessageId, null, null),
            PushSendStatus.PermanentFailure =>
                new(DeliveryAction.MarkFailed, null, result.LastError, null),
            _ when attemptCount + 1 >= maxAttempts =>
                new(DeliveryAction.MarkFailed, null, result.LastError, null),
            _ =>
                new(DeliveryAction.ScheduleRetry, null, result.LastError,
                    CalculateRetryDelay(attemptCount + 1))
        };

    // ─── BackgroundService ───────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NotificationDispatcher started");

        if (!fcmSender.IsAvailable)
        {
            logger.LogError(
                "NotificationDispatcher: FCM sender is unavailable — push notifications will not be delivered. " +
                "Resolve Firebase credential issues and restart the service.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingNotificationsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "NotificationDispatcher error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessPendingNotificationsAsync(CancellationToken ct)
    {
        using var activity = KararTelemetry.StartActivity("notification_dispatcher.process_batch");
        activity?.SetTag("messaging.system", "fcm");

        if (!fcmSender.IsAvailable)
            return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var fetchCmd = new NpgsqlCommand(
            """
            SELECT n.id, n.device_id, n.type, n.title, n.body, n.post_id, d.created_at, n.attempt_count, n.dedupe_key, u.id AS user_id
            FROM notifications n
            JOIN devices d ON d.id = n.device_id
            LEFT JOIN users u ON u.device_id = n.device_id AND u.deleted_at IS NULL
            WHERE n.sent_at IS NULL
              AND n.failed_at IS NULL
              AND (n.next_attempt_at IS NULL OR n.next_attempt_at <= NOW())
            ORDER BY COALESCE(n.next_attempt_at, n.created_at) ASC, n.created_at ASC
            LIMIT 50
            FOR UPDATE SKIP LOCKED
            """,
            connection);

        var batch = new List<PendingNotification>();

        await using (var tx = await connection.BeginTransactionAsync(ct))
        {
            fetchCmd.Transaction = tx;
            await using (var reader = await fetchCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    batch.Add(new PendingNotification(
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetGuid(5),
                        reader.GetFieldValue<DateTimeOffset>(6),
                        reader.GetInt32(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8),
                        reader.IsDBNull(9) ? null : reader.GetGuid(9)));
                }
            }

            if (batch.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            var sendable = new List<Guid>();
            var deferred = new List<(Guid Id, TimeSpan? Delay)>();
            foreach (var notification in batch)
            {
                var priority = NotificationRateLimiter.GetPriority(notification.Type);

                var decision = await preferenceRouter.CanPushAsync(
                    notification.DeviceId,
                    notification.Type,
                    priority,
                    connection,
                    ct);

                if (!decision.Allowed)
                {
                    deferred.Add((notification.Id, decision.SuggestedRetryDelay));
                    await LogEventAsync(connection, notification.Id, notification.DeviceId, "suppressed",
                        decision.SuggestedRetryDelay.HasValue
                            ? $"{{\"reason\":\"preference_blocked\",\"retry_in_seconds\":{(int)decision.SuggestedRetryDelay.Value.TotalSeconds}}}"
                            : "{\"reason\":\"preference_blocked\"}",
                        ct);
                    continue;
                }

                var canSend = await rateLimiter.CanSendAsync(
                    notification.DeviceId,
                    priority,
                    notification.DeviceCreatedAt,
                    ct);

                if (canSend)
                    sendable.Add(notification.Id);
                else
                    deferred.Add((notification.Id, null));
            }

            if (deferred.Count > 0)
                await DeferNotificationsAsync(connection, tx, deferred, ct);

            if (sendable.Count == 0)
            {
                await tx.CommitAsync(ct);
                return;
            }

            batch = batch.Where(n => sendable.Contains(n.Id)).ToList();
            await ReserveNotificationsAsync(connection, tx, sendable, ct);
            await tx.CommitAsync(ct);
        }

        foreach (var notification in batch)
        {
            // Redis lock — prevents double-send if two instances raced past the DB lock.
            var lockKey = $"notif:lock:{notification.Id:N}";
            var lockAcquired = await redis.GetDb().StringSetAsync(
                lockKey, "1", TimeSpan.FromMinutes(5), StackExchange.Redis.When.NotExists);
            if (!lockAcquired)
            {
                logger.LogDebug("Notification {Id} already locked by another instance, skipping", notification.Id);
                continue;
            }

            // Dedup check — skip if this dedupe_key was sent within the last 24 h.
            if (notification.DedupeKey is not null)
            {
                var dedupRedisKey = $"notif:dedup:{notification.DedupeKey}";
                var alreadySent = await redis.GetDb().KeyExistsAsync(dedupRedisKey);
                if (alreadySent)
                {
                    await MarkDedupSuppressedAsync(connection, notification, ct);
                    continue;
                }
            }

            await LogEventAsync(connection, notification.Id, notification.DeviceId, "send_attempt", ct: ct);

            var badgeCount = await GetUnreadCountAsync(connection, notification.DeviceId, ct);
            var result = await SendPushAsync(
                connection,
                notification.DeviceId,
                notification.Title,
                notification.Body,
                notification.Type,
                notification.PostId,
                badgeCount,
                ct);

            await UpdateDeliveryStateAsync(connection, notification, result, ct);

            // Stamp dedup key only on confirmed delivery to FCM.
            if (result.Status == PushSendStatus.Success && notification.DedupeKey is not null)
            {
                await redis.GetDb().StringSetAsync(
                    $"notif:dedup:{notification.DedupeKey}", "1", TimeSpan.FromHours(24));
            }
        }
    }

    private static async Task<int> GetUnreadCountAsync(NpgsqlConnection connection, Guid deviceId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT COUNT(*) FROM notifications WHERE device_id = @deviceId AND is_read = FALSE AND dismissed_at IS NULL",
            connection);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    private async Task<PushSendResult> SendPushAsync(
        NpgsqlConnection connection,
        Guid deviceId,
        string title,
        string body,
        string type,
        Guid? postId,
        int badgeCount,
        CancellationToken ct)
    {
        using var activity = KararTelemetry.StartActivity("notification_dispatcher.send_push");
        activity?.SetTag("messaging.system", "fcm");
        activity?.SetTag("karar.notification_type", type);

        var tokens = await GetFcmTokensAsync(connection, deviceId, ct);
        if (tokens.Count == 0)
            return PushSendResult.PermanentFailure("no_fcm_token");

        var staleTokens = new List<string>();
        var failures = new List<string>();
        string? providerMessageId = null;

        foreach (var token in tokens)
        {
            try
            {
                var deepLink = BuildDeepLink(type, postId);
                var message = new Message
                {
                    Token = token,
                    Notification = new Notification { Title = title, Body = body },
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = type,
                        ["referenceId"] = postId?.ToString() ?? string.Empty,
                        ["deepLink"] = deepLink,
                        ["click_action"] = "FLUTTER_NOTIFICATION_CLICK",
                    },
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification
                        {
                            Sound = "default",
                            ChannelId = GetAndroidChannelId(type),
                        },
                    },
                    Apns = new ApnsConfig
                    {
                        Aps = new Aps
                        {
                            Sound = "default",
                            Badge = badgeCount,
                            MutableContent = true,
                            Category = GetApnsCategory(type),
                        },
                    },
                };

                providerMessageId ??= await fcmSender.SendAsync(message, ct);
            }
            catch (FirebaseMessagingException ex) when
                (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
            {
                staleTokens.Add(token);
            }
            catch (Exception ex)
            {
                failures.Add(ex.GetType().Name);
                logger.LogWarning(ex, "FCM send failed for token ...{Suffix}", TokenSuffix(token));
            }
        }

        if (staleTokens.Count > 0)
            await DeleteStaleTokensAsync(connection, staleTokens, ct);

        if (providerMessageId is not null)
            return PushSendResult.Success(providerMessageId);

        if (failures.Count == 0 && staleTokens.Count > 0)
            return PushSendResult.PermanentFailure("all_tokens_unregistered");

        return PushSendResult.TransientFailure(
            failures.Count == 0 ? "fcm_send_failed" : string.Join(",", failures.Distinct()));
    }

    private static async Task<List<string>> GetFcmTokensAsync(
        NpgsqlConnection connection, Guid deviceId, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT token FROM fcm_tokens WHERE device_id = @deviceId",
            connection);
        cmd.Parameters.AddWithValue("deviceId", deviceId);

        var tokens = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            tokens.Add(reader.GetString(0));

        return tokens;
    }

    private static async Task DeleteStaleTokensAsync(
        NpgsqlConnection connection, List<string> tokens, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM fcm_tokens WHERE token = ANY(@tokens)",
            connection);
        cmd.Parameters.AddWithValue("tokens", tokens.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task ReserveNotificationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        List<Guid> notificationIds,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE notifications
            SET next_attempt_at = NOW() + (@reservationSeconds * INTERVAL '1 second'),
                last_error = NULL
            WHERE id = ANY(@ids)
              AND sent_at IS NULL
              AND failed_at IS NULL
            """,
            connection,
            tx);
        cmd.Parameters.AddWithValue("reservationSeconds", (int)ReservationTtl.TotalSeconds);
        cmd.Parameters.AddWithValue("ids", notificationIds.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task DeferNotificationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction tx,
        List<(Guid Id, TimeSpan? Delay)> deferred,
        CancellationToken ct)
    {
        foreach (var group in deferred.GroupBy(d => (int)(d.Delay ?? RateLimitDelay).TotalSeconds))
        {
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE notifications
                SET next_attempt_at = NOW() + (@delaySeconds * INTERVAL '1 second'),
                    last_error = 'rate_limited'
                WHERE id = ANY(@ids)
                  AND sent_at IS NULL
                  AND failed_at IS NULL
                """,
                connection,
                tx);
            cmd.Parameters.AddWithValue("delaySeconds", group.Key);
            cmd.Parameters.AddWithValue("ids", group.Select(d => d.Id).ToArray());
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private async Task UpdateDeliveryStateAsync(
        NpgsqlConnection connection,
        PendingNotification notification,
        PushSendResult result,
        CancellationToken ct)
    {
        var decision = DetermineDeliveryDecision(result, notification.AttemptCount, MaxAttempts);

        if (decision.Action == DeliveryAction.MarkSent)
        {
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE notifications
                SET sent_at = NOW(),
                    provider_message_id = @providerMessageId,
                    next_attempt_at = NULL,
                    last_error = NULL,
                    failed_at = NULL,
                    status = 'sent'
                WHERE id = @id
                """,
                connection);
            cmd.Parameters.AddWithValue("id", notification.Id);
            cmd.Parameters.AddWithValue("providerMessageId", decision.ProviderMessageId!);
            await cmd.ExecuteNonQueryAsync(ct);
            await LogEventAsync(connection, notification.Id, notification.DeviceId, "sent",
                $"{{\"provider_message_id\":\"{decision.ProviderMessageId}\"}}", ct);
            sloMetrics.RecordNotificationDelivery("sent", notification.Type);

            // SSE fan-out: notify the user's active browser/app connections
            if (notification.UserId is { } userId)
            {
                var unreadCount = await GetUnreadCountAsync(connection, notification.DeviceId, ct);
                await redis.PublishUserEventAsync(userId, new
                {
                    type = "notification.created",
                    notificationId = notification.Id,
                    unreadCount
                });
            }

            return;
        }

        if (decision.Action == DeliveryAction.MarkFailed)
        {
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE notifications
                SET attempt_count = attempt_count + 1,
                    last_error = @lastError,
                    failed_at = NOW(),
                    next_attempt_at = NULL,
                    status = 'permanently_failed'
                WHERE id = @id
                  AND sent_at IS NULL
                """,
                connection);
            cmd.Parameters.AddWithValue("id", notification.Id);
            cmd.Parameters.AddWithValue("lastError", decision.Error!);
            await cmd.ExecuteNonQueryAsync(ct);
            await InsertDeadLetterAsync(connection, notification, decision.Error!, ct);
            await LogEventAsync(connection, notification.Id, notification.DeviceId, "failed",
                $"{{\"error\":\"{decision.Error}\"}}", ct);
            sloMetrics.RecordNotificationDelivery("failed", notification.Type);
            return;
        }

        // ScheduleRetry — sent_at is never touched
        await using var retryCmd = new NpgsqlCommand(
            """
            UPDATE notifications
            SET attempt_count = attempt_count + 1,
                last_error = @lastError,
                next_attempt_at = NOW() + (@delaySeconds * INTERVAL '1 second')
            WHERE id = @id
              AND sent_at IS NULL
              AND failed_at IS NULL
            """,
            connection);
        retryCmd.Parameters.AddWithValue("id", notification.Id);
        retryCmd.Parameters.AddWithValue("lastError", decision.Error!);
        retryCmd.Parameters.AddWithValue("delaySeconds", (int)decision.RetryDelay!.Value.TotalSeconds);
        await retryCmd.ExecuteNonQueryAsync(ct);
        await LogEventAsync(connection, notification.Id, notification.DeviceId, "retrying",
            $"{{\"error\":\"{decision.Error}\",\"delay_seconds\":{(int)decision.RetryDelay.Value.TotalSeconds}}}", ct);
        sloMetrics.RecordNotificationDelivery("retrying", notification.Type);
    }

    private async Task MarkDedupSuppressedAsync(
        NpgsqlConnection connection,
        PendingNotification notification,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE notifications
            SET failed_at = NOW(),
                last_error = 'deduplicated',
                next_attempt_at = NULL,
                status = 'permanently_failed'
            WHERE id = @id
              AND sent_at IS NULL
            """,
            connection);
        cmd.Parameters.AddWithValue("id", notification.Id);
        await cmd.ExecuteNonQueryAsync(ct);
        await LogEventAsync(connection, notification.Id, notification.DeviceId, "suppressed",
            "{\"reason\":\"deduplicated\"}", ct);
        sloMetrics.RecordNotificationDelivery("failed", notification.Type);
    }

    private async Task InsertDeadLetterAsync(
        NpgsqlConnection connection,
        PendingNotification notification,
        string error,
        CancellationToken ct)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO dead_letter_notifications
                    (notification_id, device_id, type, title, body, post_id, attempt_count, last_error, reason)
                VALUES
                    (@notificationId, @deviceId, @type, @title, @body, @postId, @attemptCount, @lastError, @reason)
                """,
                connection);
            cmd.Parameters.AddWithValue("notificationId", notification.Id);
            cmd.Parameters.AddWithValue("deviceId", notification.DeviceId);
            cmd.Parameters.AddWithValue("type", notification.Type);
            cmd.Parameters.AddWithValue("title", notification.Title);
            cmd.Parameters.AddWithValue("body", notification.Body);
            cmd.Parameters.AddWithValue("postId", notification.PostId.HasValue ? (object)notification.PostId.Value : DBNull.Value);
            cmd.Parameters.AddWithValue("attemptCount", notification.AttemptCount + 1);
            cmd.Parameters.AddWithValue("lastError", (object?)error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("reason", "max_attempts_exceeded");
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to insert dead letter for notification {NotificationId}", notification.Id);
        }
    }

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
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to log notification event {EventType} for {NotificationId}", eventType, notificationId);
        }
    }

    public static string GetAndroidChannelId(string type) => type switch
    {
        "comment_on_post" => "comments",
        "reply_on_comment" or "mention" or "follow" => "mentions",
        "verdict_milestone" or "viral_post_owner" => "milestones",
        "trend_alert" or "follow_new_post" => "viral",
        "weekly_digest" => "digest",
        _ => "system",
    };

    public static string GetApnsCategory(string type) => type switch
    {
        "comment_on_post" => "COMMENT",
        "reply_on_comment" => "REPLY",
        "mention" => "MENTION",
        "follow" => "FOLLOW",
        "verdict_milestone" or "viral_post_owner" => "MILESTONE",
        "trend_alert" or "follow_new_post" => "TREND",
        "weekly_digest" => "DIGEST",
        "moderation_result" => "MODERATION",
        "system_announcement" => "SYSTEM",
        _ => "GENERAL",
    };

    public static string BuildDeepLink(string type, Guid? postId) => type switch
    {
        "comment_on_post" or "reply_on_comment" or "mention"
            or "verdict_milestone" or "viral_post_owner"
            or "trend_alert" or "follow_new_post"
            when postId.HasValue => $"/posts/{postId}",
        "weekly_digest" => "/notifications",
        "moderation_result" => "/profile",
        "system_announcement" => "/notifications",
        _ => "/notifications",
    };

    private static string TokenSuffix(string token) =>
        token.Length <= 6 ? token : token[^6..];

    // ─── Types ───────────────────────────────────────────────────────────────

    private sealed record PendingNotification(
        Guid Id,
        Guid DeviceId,
        string Type,
        string Title,
        string Body,
        Guid? PostId,
        DateTimeOffset DeviceCreatedAt,
        int AttemptCount,
        string? DedupeKey,
        Guid? UserId);

    public enum PushSendStatus { Success, TransientFailure, PermanentFailure }

    public sealed record PushSendResult(
        PushSendStatus Status,
        string? ProviderMessageId,
        string LastError)
    {
        public static PushSendResult Success(string providerMessageId) =>
            new(PushSendStatus.Success, providerMessageId, string.Empty);

        public static PushSendResult TransientFailure(string error) =>
            new(PushSendStatus.TransientFailure, null, error);

        public static PushSendResult PermanentFailure(string error) =>
            new(PushSendStatus.PermanentFailure, null, error);
    }

    public enum DeliveryAction { MarkSent, MarkFailed, ScheduleRetry }

    public readonly record struct DeliveryDecision(
        DeliveryAction Action,
        string? ProviderMessageId,
        string? Error,
        TimeSpan? RetryDelay);
}
