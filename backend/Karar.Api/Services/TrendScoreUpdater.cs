using Npgsql;

namespace Karar.Api.Services;

public sealed class TrendScoreUpdater(
    IConfiguration configuration,
    ILogger<TrendScoreUpdater> logger,
    RedisService redis)
    : BackgroundService
{
    private readonly string _connectionString = configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing.");
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2); // Reduced interval for distributed updates

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("TrendScoreUpdater started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Önce "dirty" (etkileşim almış) postları hızla güncelle
                await UpdateDirtyTrendScoresAsync(stoppingToken);

                // 2. Her 10 dakikada bir tüm aktif postları (zaman aşımı cezası için) güncelle
                if (DateTime.UtcNow.Minute % 10 == 0)
                {
                    await UpdateAllActiveTrendScoresAsync(stoppingToken);
                }
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

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var updates = await ComputeSmoothedScoresAsync(connection, dirtyPostIds, ct);
        if (updates.Count > 0)
        {
            await BatchUpdateScoresAsync(connection, updates, ct);
            logger.LogDebug("{Count} adet etkileşimli postun trend skoru güncellendi", updates.Count);
        }
    }

    private async Task UpdateAllActiveTrendScoresAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        // Get all active post IDs for bulk processing
        await using var idCmd = new NpgsqlCommand("SELECT id FROM posts WHERE status = 'active'", connection);
        var allIds = new List<Guid>();
        await using (var r = await idCmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct)) allIds.Add(r.GetGuid(0));
        }

        var updates = await ComputeSmoothedScoresAsync(connection, allIds, ct);
        if (updates.Count > 0)
        {
            await BatchUpdateScoresAsync(connection, updates, ct);
            logger.LogDebug("Tüm aktif postların ({Count}) trend skorları tazelendi", updates.Count);
        }
    }

    // Uses COUNT(DISTINCT device fingerprint) to deduplicate coordinated voting,
    // then applies EWMA velocity smoothing (alpha=0.3) to dampen sudden spikes.
    private async Task<List<(Guid Id, double Score)>> ComputeSmoothedScoresAsync(
        NpgsqlConnection connection,
        List<Guid> postIds,
        CancellationToken ct)
    {
        if (postIds.Count == 0) return [];

        // Fingerprint-deduplicated vote counts per post
        await using var cmd = new NpgsqlCommand(
            """
            SELECT p.id,
                   COUNT(DISTINCT CASE WHEN v.vote_type = 'hakli' THEN d.fingerprint END)::int AS unique_hakli,
                   COUNT(DISTINCT CASE WHEN v.vote_type = 'haksiz' THEN d.fingerprint END)::int AS unique_haksiz,
                   p.comment_count,
                   p.created_at,
                   COALESCE(pvd.avg_dwell_seconds, 0) AS avg_dwell_seconds,
                   COALESCE(pvd.total_exposures, 0)::int AS total_exposures
            FROM posts p
            LEFT JOIN votes v ON v.post_id = p.id AND v.is_quarantined = FALSE
            LEFT JOIN devices d ON d.id = v.device_id AND NOT d.is_banned
            LEFT JOIN (
                SELECT post_id,
                       SUM(dwell_seconds_total)::double precision / NULLIF(SUM(dwell_count), 0) AS avg_dwell_seconds,
                       SUM(view_count) AS total_exposures
                FROM post_views
                GROUP BY post_id
            ) pvd ON pvd.post_id = p.id
            WHERE p.id = ANY(@ids) AND p.status = 'active'
            GROUP BY p.id, p.comment_count, p.created_at, pvd.avg_dwell_seconds, pvd.total_exposures
            """,
            connection);
        cmd.Parameters.AddWithValue("ids", postIds.ToArray());

        const double Alpha = 0.3;
        var updates = new List<(Guid Id, double Score)>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            var hakli = reader.GetInt32(1);
            var haksiz = reader.GetInt32(2);
            var comments = reader.GetInt32(3);
            var createdAt = reader.GetFieldValue<DateTimeOffset>(4);
            var averageDwellSeconds = reader.GetDouble(5);
            var totalExposures = reader.GetInt32(6);
            var ageHours = (DateTimeOffset.UtcNow - createdAt).TotalHours;

            // Total unique voters — use for EWMA velocity computation
            var totalUniqueVotes = hakli + haksiz;

            // EWMA velocity smoothing
            var (prevVotes, prevEwma) = await redis.GetTrendVelocityAsync(id);
            var delta = Math.Max(0, totalUniqueVotes - prevVotes);
            var newEwma = Alpha * delta + (1 - Alpha) * prevEwma;
            await redis.SetTrendVelocityAsync(id, totalUniqueVotes, newEwma);

            // Smooth the effective vote count: blend raw unique count with EWMA-smoothed count
            var effectiveVotes = (int)Math.Round(totalUniqueVotes * 0.7 + newEwma * 0.3);
            var score = TrendScoreCalculator.Compute(
                hakli > 0 ? Math.Max(1, (int)(effectiveVotes * hakli / (double)Math.Max(1, totalUniqueVotes))) : 0,
                haksiz > 0 ? Math.Max(1, (int)(effectiveVotes * haksiz / (double)Math.Max(1, totalUniqueVotes))) : 0,
                comments,
                ageHours,
                averageDwellSeconds,
                totalExposures
            );
            updates.Add((id, score));
        }

        return updates;
    }

    private async Task BatchUpdateScoresAsync(NpgsqlConnection conn, List<(Guid Id, double Score)> updates, CancellationToken ct)
    {
        await using var updateCmd = new NpgsqlCommand(
            """
            UPDATE posts
            SET trend_score = u.score, updated_at = NOW()
            FROM unnest(@ids, @scores) AS u(id uuid, score double precision)
            WHERE posts.id = u.id
            """,
            conn);

        updateCmd.Parameters.AddWithValue("ids", updates.Select(u => u.Id).ToArray());
        updateCmd.Parameters.AddWithValue("scores", updates.Select(u => u.Score).ToArray());

        await updateCmd.ExecuteNonQueryAsync(ct);
    }
}
