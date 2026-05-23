using FirebaseAdmin.Messaging;
using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// DB outbox pattern: every 30 s, picks up unsent notifications, looks up the
/// device's FCM token(s), sends the push, then stamps sent_at.
/// Falls back silently when Firebase is not initialised (dev environment).
/// </summary>
public sealed class NotificationDispatcher(
    IConfiguration configuration,
    ILogger<NotificationDispatcher> logger,
    NotificationRateLimiter rateLimiter)
    : BackgroundService
{
    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

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
        if (FirebaseAdmin.FirebaseApp.DefaultInstance is null) return;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Fetch up to 50 unsent notifications, lock them to avoid duplicate sends.
        await using var fetchCmd = new NpgsqlCommand(
            """
            SELECT n.id, n.device_id, n.type, n.title, n.body, n.post_id, d.created_at
            FROM notifications n
            JOIN devices d ON d.id = n.device_id
            WHERE n.sent_at IS NULL
            ORDER BY n.created_at ASC
            LIMIT 50
            FOR UPDATE SKIP LOCKED
            """,
            connection);

        var batch = new List<(Guid Id, Guid DeviceId, string Type, string Title, string Body, Guid? PostId, DateTimeOffset DeviceCreatedAt)>();

        await using (var tx = await connection.BeginTransactionAsync(ct))
        {
            fetchCmd.Transaction = tx;
            await using (var reader = await fetchCmd.ExecuteReaderAsync(ct))
            {
                while (await reader.ReadAsync(ct))
                {
                    batch.Add((
                        reader.GetGuid(0),
                        reader.GetGuid(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.IsDBNull(5) ? null : reader.GetGuid(5),
                        reader.GetFieldValue<DateTimeOffset>(6)
                    ));
                }
            }

            if (batch.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            var sendable = new List<Guid>();
            foreach (var notification in batch)
            {
                var canSend = await rateLimiter.CanSendAsync(
                    notification.DeviceId,
                    NotificationRateLimiter.GetPriority(notification.Type),
                    notification.DeviceCreatedAt,
                    ct);

                if (canSend)
                {
                    sendable.Add(notification.Id);
                }
            }

            if (sendable.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return;
            }

            batch = batch.Where(n => sendable.Contains(n.Id)).ToList();

            // Mark sendable notifications as sent immediately so other dispatcher instances skip them.
            await using var markCmd = new NpgsqlCommand(
                "UPDATE notifications SET sent_at = NOW() WHERE id = ANY(@ids)",
                connection, tx);
            markCmd.Parameters.AddWithValue("ids", sendable.ToArray());
            await markCmd.ExecuteNonQueryAsync(ct);
            await tx.CommitAsync(ct);
        }

        // Send FCM outside the transaction.
        foreach (var notif in batch)
        {
            await SendPushAsync(connection, notif.DeviceId, notif.Title, notif.Body, notif.Type, notif.PostId, ct);
        }
    }

    private async Task SendPushAsync(
        NpgsqlConnection connection,
        Guid deviceId,
        string title,
        string body,
        string type,
        Guid? postId,
        CancellationToken ct)
    {
        var tokens = await GetFcmTokensAsync(connection, deviceId, ct);
        if (tokens.Count == 0) return;

        var staleTokens = new List<string>();

        foreach (var token in tokens)
        {
            try
            {
                var message = new Message
                {
                    Token = token,
                    Notification = new Notification { Title = title, Body = body },
                    Data = new Dictionary<string, string>
                    {
                        ["type"] = type,
                        ["referenceId"] = postId?.ToString() ?? string.Empty,
                        ["click_action"] = "FLUTTER_NOTIFICATION_CLICK",
                    },
                    Android = new AndroidConfig
                    {
                        Priority = Priority.High,
                        Notification = new AndroidNotification { Sound = "default" },
                    },
                    Apns = new ApnsConfig
                    {
                        Aps = new Aps { Sound = "default", Badge = 1 },
                    },
                };

                await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
            }
            catch (FirebaseMessagingException ex) when
                (ex.MessagingErrorCode == MessagingErrorCode.Unregistered)
            {
                staleTokens.Add(token);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "FCM send failed for token …{Suffix}", token[^6..]);
            }
        }

        if (staleTokens.Count > 0)
            await DeleteStaleTokensAsync(connection, staleTokens, ct);
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
}
