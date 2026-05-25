using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// Periodically detects posts that have reached viral velocity and queues
/// a viral_post_owner notification for the post owner.
///
/// Criteria: distribution_stage >= 3, >= 50 total votes, active status,
/// created within 48 hours, report_count &#x3C;= 5.
/// Deduplication: Redis key per post (7-day TTL) prevents repeat notifications.
/// Frequency cap: max 3 viral notifications per device per 24 hours.
/// </summary>
public sealed class ViralNotificationJob(
    IConfiguration configuration,
    RedisService redis,
    ILogger<ViralNotificationJob> logger)
    : BackgroundService
{
    private const int MinVotesForViral = 50;
    private const int MaxReportCount = 5;
    private const int MaxViralNotificationsPerDay = 3;
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DedupeExpiry = TimeSpan.FromDays(7);
    private static readonly TimeSpan FrequencyCapExpiry = TimeSpan.FromHours(24);

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

        await using var cmd = new NpgsqlCommand(
            """
            SELECT p.id,
                   p.device_id,
                   p.vote_count_hakli,
                   p.vote_count_haksiz
            FROM posts p
            WHERE p.status = 'active'
              AND p.is_unlisted = FALSE
              AND p.distribution_stage >= 3
              AND p.created_at > NOW() - INTERVAL '48 hours'
              AND p.vote_count_hakli + p.vote_count_haksiz >= @minVotes
              AND p.report_count <= @maxReports
            ORDER BY p.vote_count_hakli + p.vote_count_haksiz DESC
            LIMIT 30
            """,
            connection);
        cmd.Parameters.AddWithValue("minVotes", MinVotesForViral);
        cmd.Parameters.AddWithValue("maxReports", MaxReportCount);

        var candidates = new List<(Guid PostId, Guid DeviceId, int Hakli, int Haksiz)>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                candidates.Add((
                    reader.GetGuid(0),
                    reader.GetGuid(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3)));
            }
        }

        if (candidates.Count == 0) return;

        foreach (var (postId, deviceId, hakli, haksiz) in candidates)
        {
            ct.ThrowIfCancellationRequested();

            if (!await TryReserveAsync(postId)) continue;

            if (!await CheckFrequencyCapAsync(deviceId))
            {
                // Release the reservation so we can try again tomorrow
                await redis.GetDb().KeyDeleteAsync($"viral_notif:{postId:N}");
                continue;
            }

            var totalVotes = hakli + haksiz;
            var hakliPercent = totalVotes > 0 ? (int)Math.Round(hakli * 100.0 / totalVotes) : 0;
            await InsertNotificationAsync(connection, deviceId, postId, totalVotes, hakliPercent, ct);

            logger.LogInformation(
                "Viral notification queued for post {PostId} (votes={Votes}, hakli={Pct}%)",
                postId, totalVotes, hakliPercent);
        }
    }

    private async Task<bool> TryReserveAsync(Guid postId)
    {
        var key = $"viral_notif:{postId:N}";
        return await redis.GetDb().StringSetAsync(key, "1", DedupeExpiry, when: StackExchange.Redis.When.NotExists);
    }

    private async Task<bool> CheckFrequencyCapAsync(Guid deviceId)
    {
        var key = $"viral_notif_cap:{deviceId:N}:{DateTime.UtcNow:yyyyMMdd}";
        var count = await redis.GetDb().StringIncrementAsync(key);
        if (count == 1)
        {
            await redis.GetDb().KeyExpireAsync(key, FrequencyCapExpiry);
        }
        return count <= MaxViralNotificationsPerDay;
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
            VALUES (@deviceId, 'viral_post_owner', 'Postun trend oldu!', @body, @postId)
            """,
            connection);
        cmd.Parameters.AddWithValue("deviceId", deviceId);
        cmd.Parameters.AddWithValue("body",
            $"{totalVotes} kişi oyladı, %{hakliPercent} Haklı buluyor. Topluluğun gündemine girdin!");
        cmd.Parameters.AddWithValue("postId", postId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
