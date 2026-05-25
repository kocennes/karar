using FirebaseAdmin.Messaging;
using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// DB outbox pattern: every 30 s, picks up due notifications, sends FCM push,
/// then stamps sent_at only after the provider accepts the message.
/// </summary>
public sealed class NotificationDispatcher(
    IConfiguration configuration,
    ILogger<NotificationDispatcher> logger,
    NotificationRateLimiter rateLimiter,
    NotificationPreferenceRouter preferenceRouter)
    : BackgroundService
{
    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RateLimitDelay = TimeSpan.FromMinutes(15);
    private const int MaxAttempts = 5;
    private bool _firebaseMissingLogged;

    public static TimeSpan CalculateRetryDelay(int attemptCount)
    {
        var exponent = Math.Clamp(attemptCount - 1, 0, 6);
        return TimeSpan.FromMinutes(Math.Pow(2, exponent));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("NotificationDispatcher started");

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
        if (FirebaseAdmin.FirebaseApp.DefaultInstance is null)
        {
            if (!_firebaseMissingLogged)
            {
                logger.LogWarning("NotificationDispatcher skipped because Firebase Admin is not initialized");
                _firebaseMissingLogged = true;
            }

            return;
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var fetchCmd = new NpgsqlCommand(
            """
            SELECT n.id, n.device_id, n.type, n.title, n.body, n.post_id, d.created_at, n.attempt_count
            FROM notifications n
            JOIN devices d ON d.id = n.device_id
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
                        reader.GetInt32(7)));
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
                    continue;
                }

                var canSend = await rateLimiter.CanSendAsync(
                    notification.DeviceId,
                    priority,
                    notification.DeviceCreatedAt,
                    ct);

                if (canSend)
                {
                    sendable.Add(notification.Id);
                }
                else
                {
                    deferred.Add((notification.Id, null));
                }
            }

            if (deferred.Count > 0)
            {
                await DeferNotificationsAsync(connection, tx, deferred, ct);
            }

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
        var tokens = await GetFcmTokensAsync(connection, deviceId, ct);
        if (tokens.Count == 0)
        {
            return PushSendResult.PermanentFailure("no_fcm_token");
        }

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

                providerMessageId ??= await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
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
        {
            await DeleteStaleTokensAsync(connection, staleTokens, ct);
        }

        if (providerMessageId is not null)
        {
            return PushSendResult.Success(providerMessageId);
        }

        if (failures.Count == 0 && staleTokens.Count > 0)
        {
            return PushSendResult.PermanentFailure("all_tokens_unregistered");
        }

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
        {
            tokens.Add(reader.GetString(0));
        }

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
        // Group by delay bucket to minimize round-trips
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

    private static async Task UpdateDeliveryStateAsync(
        NpgsqlConnection connection,
        PendingNotification notification,
        PushSendResult result,
        CancellationToken ct)
    {
        if (result.Status == PushSendStatus.Success)
        {
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE notifications
                SET sent_at = NOW(),
                    provider_message_id = @providerMessageId,
                    next_attempt_at = NULL,
                    last_error = NULL,
                    failed_at = NULL
                WHERE id = @id
                """,
                connection);
            cmd.Parameters.AddWithValue("id", notification.Id);
            cmd.Parameters.AddWithValue("providerMessageId", result.ProviderMessageId!);
            await cmd.ExecuteNonQueryAsync(ct);
            return;
        }

        if (result.Status == PushSendStatus.PermanentFailure || notification.AttemptCount + 1 >= MaxAttempts)
        {
            await using var cmd = new NpgsqlCommand(
                """
                UPDATE notifications
                SET attempt_count = attempt_count + 1,
                    last_error = @lastError,
                    failed_at = NOW(),
                    next_attempt_at = NULL
                WHERE id = @id
                  AND sent_at IS NULL
                """,
                connection);
            cmd.Parameters.AddWithValue("id", notification.Id);
            cmd.Parameters.AddWithValue("lastError", result.LastError);
            await cmd.ExecuteNonQueryAsync(ct);
            return;
        }

        var retryDelay = CalculateRetryDelay(notification.AttemptCount + 1);
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
        retryCmd.Parameters.AddWithValue("lastError", result.LastError);
        retryCmd.Parameters.AddWithValue("delaySeconds", (int)retryDelay.TotalSeconds);
        await retryCmd.ExecuteNonQueryAsync(ct);
    }

    public static string GetAndroidChannelId(string type) => type switch
    {
        "comment_on_post" => "comments",
        "reply_on_comment" or "mention" => "mentions",
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
        "verdict_milestone" or "viral_post_owner" => "MILESTONE",
        "moderation_result" => "MODERATION",
        "system_announcement" => "SYSTEM",
        _ => "GENERAL",
    };

    public static string BuildDeepLink(string type, Guid? postId) => type switch
    {
        "comment_on_post" or "reply_on_comment" or "mention"
            or "verdict_milestone" or "viral_post_owner"
            when postId.HasValue => $"/posts/{postId}",
        "weekly_digest" => "/notifications",
        "moderation_result" => "/profile",
        "system_announcement" => "/notifications",
        _ => "/notifications",
    };

    private static string TokenSuffix(string token) =>
        token.Length <= 6 ? token : token[^6..];

    private sealed record PendingNotification(
        Guid Id,
        Guid DeviceId,
        string Type,
        string Title,
        string Body,
        Guid? PostId,
        DateTimeOffset DeviceCreatedAt,
        int AttemptCount);

    private enum PushSendStatus
    {
        Success,
        TransientFailure,
        PermanentFailure
    }

    private sealed record PushSendResult(
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
}
