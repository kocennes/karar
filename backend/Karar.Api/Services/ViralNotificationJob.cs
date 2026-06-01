using System.Text.Json;
using Karar.Api.Contracts;
using Karar.Api.Models;
using Npgsql;

namespace Karar.Api.Services;

/// Detects posts with viral velocity and notifies their owners.
///
/// Criteria (all must be true):
///   • ≥ 20 new votes in the last 30 minutes (velocity, not total)
///   • Post status is 'active' (not under_review or removed)
///   • Post owner has notifyOnTrend = true in notification_preferences
///   • No viral notification sent for this post in the last 2 hours (Redis TTL)
///
/// Runs every 30 minutes.
public sealed class ViralNotificationJob(
    IConfiguration configuration,
    RedisService redis,
    ILogger<ViralNotificationJob> logger)
    : BackgroundService
{
    private const int MinVelocityVotes = 20;
    private static readonly TimeSpan VelocityWindow = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DedupeExpiry = TimeSpan.FromHours(2);

    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ViralNotificationJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "ViralNotificationJob error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Find posts whose vote velocity (new votes in last 30 min) meets the threshold.
        // Exclude posts that are under_review or removed (only 'active' status).
        // Also fetch the owner's notification_preferences to check notifyOnTrend.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT p.id,
                   p.device_id,
                   (p.vote_count_hakli + p.vote_count_haksiz) AS total_votes,
                   p.vote_count_hakli,
                   u.notification_preferences
            FROM posts p
            LEFT JOIN users u ON u.device_id = p.device_id AND u.deleted_at IS NULL
            WHERE p.status = 'active'
              AND p.is_unlisted = FALSE
              AND (
                  SELECT COUNT(*)
                  FROM votes v
                  WHERE v.post_id = p.id
                    AND v.created_at > NOW() - @velocityWindow
              ) >= @minVelocity
            ORDER BY p.created_at DESC
            LIMIT 30
            """,
            connection);
        cmd.Parameters.AddWithValue("velocityWindow", VelocityWindow);
        cmd.Parameters.AddWithValue("minVelocity", MinVelocityVotes);

        var candidates = new List<(Guid PostId, Guid DeviceId, int TotalVotes, int Hakli, string? Prefs)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                candidates.Add((
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        if (candidates.Count == 0) return;

        foreach (var (postId, deviceId, totalVotes, hakli, prefsJson) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            // Check notifyOnTrend preference
            if (!IsNotifyOnTrendEnabled(prefsJson))
            {
                logger.LogDebug("ViralNotificationJob: skipping post {PostId} — notifyOnTrend disabled", postId);
                continue;
            }

            // 2-hour dedup: skip if we already sent a viral notification for this post recently
            if (!await TryReserveAsync(postId))
            {
                logger.LogDebug("ViralNotificationJob: skipping post {PostId} — dedup TTL active", postId);
                continue;
            }

            var hakliPercent = totalVotes > 0 ? (int)Math.Round(hakli * 100.0 / totalVotes) : 0;
            await InsertNotificationAsync(connection, deviceId, postId, totalVotes, hakliPercent, ct);

            logger.LogInformation(
                "Viral notification queued for post {PostId} (velocity≥{Min}, total={Total}, hakli={Pct}%)",
                postId, MinVelocityVotes, totalVotes, hakliPercent);
        }
    }

    private static bool IsNotifyOnTrendEnabled(string? prefsJson)
    {
        if (string.IsNullOrEmpty(prefsJson) || prefsJson == "{}") return false;
        try
        {
            var prefs = JsonSerializer.Deserialize<NotificationPreferencesRequest>(
                prefsJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return prefs?.NotifyOnTrend ?? false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryReserveAsync(Guid postId)
    {
        var key = $"viral_notif:{postId:N}";
        return await redis.GetDb().StringSetAsync(key, "1", DedupeExpiry, when: StackExchange.Redis.When.NotExists);
    }

    private static async Task InsertNotificationAsync(
        NpgsqlConnection connection,
        Guid deviceId,
        Guid postId,
        int totalVotes,
        int hakliPercent,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO notifications (device_id, type, title, body, post_id)
            VALUES (@deviceId, @type, 'Postun viral oluyor!', @body, @postId)
            """,
            connection);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        cmd.Parameters.AddWithValue("type", NotificationTypes.ViralPostOwner);
        cmd.Parameters.AddWithValue("body",
            $"Şu an {totalVotes} oy var, %{hakliPercent} Haklı buluyor!");
        cmd.Parameters.AddWithValue("postId", postId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
