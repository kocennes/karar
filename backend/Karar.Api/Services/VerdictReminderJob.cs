using Karar.Api.Models;
using Npgsql;

namespace Karar.Api.Services;

public sealed class VerdictReminderJob(
    IConfiguration configuration,
    RedisService redis,
    ILogger<VerdictReminderJob> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan SentFlagExpiry = TimeSpan.FromDays(30);

    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("VerdictReminderJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "VerdictReminderJob error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            SELECT p.id,
                   p.device_id,
                   p.vote_count_hakli,
                   p.vote_count_haksiz
            FROM posts p
            JOIN devices d ON d.id = p.device_id
            WHERE p.status = 'active'
              AND p.created_at <= NOW() - INTERVAL '6 hours'
              AND p.created_at > NOW() - INTERVAL '12 hours'
              AND p.vote_count_hakli + p.vote_count_haksiz >= 5
              AND d.last_seen_at <= NOW() - INTERVAL '2 hours'
            ORDER BY p.created_at ASC
            LIMIT 100
            """,
            connection);

        await using var reader = await command.ExecuteReaderAsync(ct);
        var candidates = new List<(Guid PostId, Guid DeviceId, int Hakli, int Haksiz)>();
        while (await reader.ReadAsync(ct))
        {
            candidates.Add((
                reader.GetGuid(0),
                reader.GetGuid(1),
                reader.GetInt32(2),
                reader.GetInt32(3)));
        }

        await reader.CloseAsync();

        foreach (var candidate in candidates)
        {
            ct.ThrowIfCancellationRequested();
            if (!await TryMarkReminderSentAsync(candidate.PostId))
            {
                continue;
            }

            var total = candidate.Hakli + candidate.Haksiz;
            var hakliPercent = CalculateHakliPercent(candidate.Hakli, candidate.Haksiz);
            await InsertReminderAsync(connection, candidate.DeviceId, candidate.PostId, total, hakliPercent, ct);
        }
    }

    public static int CalculateHakliPercent(int hakli, int haksiz)
    {
        var total = hakli + haksiz;
        return total <= 0 ? 0 : (int)Math.Round(hakli * 100.0 / total);
    }

    private async Task<bool> TryMarkReminderSentAsync(Guid postId)
    {
        var key = $"reminder_sent:{postId:N}";
        var stored = await redis.GetDb().StringSetAsync(key, "1", SentFlagExpiry, when: StackExchange.Redis.When.NotExists);
        return stored;
    }

    private static async Task InsertReminderAsync(
        NpgsqlConnection connection,
        Guid deviceId,
        Guid postId,
        int voteCount,
        int hakliPercent,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO notifications (device_id, type, title, body, post_id)
            VALUES (@deviceId, @type, 'Topluluğun karar verdi', @body, @postId)
            """,
            connection);
        command.Parameters.AddWithValue("deviceId", deviceId);
        command.Parameters.AddWithValue("type", NotificationTypes.VerdictReminder);
        command.Parameters.AddWithValue("body", $"{voteCount} kişi oyladı. %{hakliPercent} Haklı buluyor.");
        command.Parameters.AddWithValue("postId", postId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
