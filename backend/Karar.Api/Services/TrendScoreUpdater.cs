using Npgsql;

namespace Karar.Api.Services;

public sealed class TrendScoreUpdater(
    IConfiguration configuration,
    ILogger<TrendScoreUpdater> logger,
    RedisService redis)
    : BackgroundService
{
    private readonly string _connectionString = Karar.Api.Data.Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TrendScoreUpdater started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Fast path: only posts that received interactions since the last cycle
                await UpdateDirtyTrendScoresAsync(stoppingToken);

                // 2. Slow path: age-decay all active posts every 10 minutes
                if (DateTime.UtcNow.Minute % 10 == 0)
                    await UpdateAllActiveTrendScoresAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Trend skorları güncellenirken hata oluştu");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task UpdateDirtyTrendScoresAsync(CancellationToken ct)
    {
        var dirtyPostIds = await redis.GetDirtyPostsAsync(100);
        if (dirtyPostIds.Count == 0) return;

        await RefreshScoresAsync(dirtyPostIds, ct);
        logger.LogDebug("{Count} adet etkileşimli postun trend skoru güncellendi", dirtyPostIds.Count);
    }

    private async Task UpdateAllActiveTrendScoresAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var idCmd = new NpgsqlCommand(
            "SELECT id FROM posts WHERE status = 'active'", connection);
        var allIds = new List<Guid>();
        await using (var r = await idCmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct)) allIds.Add(r.GetGuid(0));

        if (allIds.Count == 0) return;
        await RefreshScoresAsync(allIds, ct);
        logger.LogDebug("Tüm aktif postların ({Count}) trend skorları tazelendi", allIds.Count);
    }

    // Delegates all computation to the pure-SQL refresh_trend_scores() function (V47 migration).
    // EWMA state (velocity + prev_votes) is persisted in the posts table itself; Redis is no
    // longer used for EWMA, only for the dirty-post queue.
    private async Task RefreshScoresAsync(List<Guid> postIds, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT refresh_trend_scores(@ids)", connection);
        cmd.Parameters.AddWithValue("ids", postIds.ToArray());
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
