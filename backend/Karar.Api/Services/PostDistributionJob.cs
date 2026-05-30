using Karar.Api.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Karar.Api.Services;

public sealed class PostDistributionJob(
    Db db,
    CategoryThrottleService categoryThrottle,
    ILogger<PostDistributionJob> logger
) : BackgroundService
{
    private const int CheckIntervalSeconds = 120;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AdvanceStagesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "[PostDistribution] Stage advancement failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(CheckIntervalSeconds), stoppingToken);
        }
    }

    private async Task AdvanceStagesAsync(CancellationToken ct)
    {
        await using var connection = await db.OpenConnectionAsync();

        var stage1Candidates = await ReadStage1CandidatesAsync(connection, ct);
        var stage1Promotions = new List<Guid>();
        foreach (var candidate in stage1Candidates)
        {
            var throttled = await categoryThrottle.IsThrottledAsync(candidate.CategoryId);
            var minAge = throttled ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10);
            var timeoutAge = throttled ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(30);
            var age = DateTimeOffset.UtcNow - candidate.CreatedAt;

            if ((age >= minAge && candidate.Votes >= 10) || age >= timeoutAge)
            {
                stage1Promotions.Add(candidate.Id);
            }
        }

        var promoted1 = await PromotePostsAsync(connection, stage1Promotions, 2, ct);
        if (promoted1 > 0)
            logger.LogInformation("[PostDistribution] {Count} posts promoted: Stage 1 -> Stage 2", promoted1);

        var stage2Candidates = await ReadStage2CandidatesAsync(connection, ct);
        var stage2Promotions = new List<Guid>();
        foreach (var candidate in stage2Candidates)
        {
            if (candidate.DistinctVoterIpBlocks < 3)
            {
                continue;
            }

            var throttled = await categoryThrottle.IsThrottledAsync(candidate.CategoryId);
            var trendThreshold = throttled ? 6.0 : 3.0;
            var voteThreshold = throttled ? 60 : 30;

            if (candidate.TrendScore > trendThreshold || candidate.Votes > voteThreshold)
            {
                stage2Promotions.Add(candidate.Id);
            }
        }

        var promoted2 = await PromotePostsAsync(connection, stage2Promotions, 3, ct);
        if (promoted2 > 0)
            logger.LogInformation("[PostDistribution] {Count} posts promoted: Stage 2 -> Stage 3", promoted2);

        await RescueColdStartPostsAsync(connection, ct);
    }

    private static async Task<IReadOnlyList<Stage1Candidate>> ReadStage1CandidatesAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT id, category_id, created_at, vote_count_hakli + vote_count_haksiz
            FROM posts
            WHERE distribution_stage = 1
              AND status = 'active'
            """,
            connection);

        var candidates = new List<Stage1Candidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            candidates.Add(new Stage1Candidate(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetInt32(3)));
        }

        return candidates;
    }

    private static async Task<IReadOnlyList<Stage2Candidate>> ReadStage2CandidatesAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT p.id,
                   p.category_id,
                   p.trend_score,
                   p.vote_count_hakli + p.vote_count_haksiz,
                   (
                       SELECT COUNT(DISTINCT v.voter_ip_block)::int
                       FROM votes v
                       WHERE v.post_id = p.id
                         AND v.voter_ip_block IS NOT NULL
                   ) AS distinct_voter_ip_blocks
            FROM posts p
            WHERE p.distribution_stage = 2
              AND p.status = 'active'
            """,
            connection);

        var candidates = new List<Stage2Candidate>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            candidates.Add(new Stage2Candidate(
                reader.GetGuid(0),
                reader.GetInt32(1),
                reader.GetDouble(2),
                reader.GetInt32(3),
                reader.GetInt32(4)));
        }

        return candidates;
    }

    private static async Task<int> PromotePostsAsync(
        NpgsqlConnection connection,
        IReadOnlyCollection<Guid> postIds,
        int stage,
        CancellationToken ct)
    {
        if (postIds.Count == 0)
        {
            return 0;
        }

        await using var command = new NpgsqlCommand(
            """
            UPDATE posts
            SET distribution_stage = @stage, updated_at = NOW()
            WHERE id = ANY(@postIds)
            """,
            connection);
        command.Parameters.AddWithValue("stage", stage);
        command.Parameters.AddWithValue("postIds", postIds.ToArray());
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task RescueColdStartPostsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE posts
            SET distribution_stage = 3, updated_at = NOW()
            WHERE distribution_stage = 2
              AND status = 'active'
              AND created_at <= NOW() - INTERVAL '2 hours'
              AND vote_count_hakli + vote_count_haksiz <= 3
            """,
            connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private sealed record Stage1Candidate(Guid Id, int CategoryId, DateTimeOffset CreatedAt, int Votes);
    private sealed record Stage2Candidate(Guid Id, int CategoryId, double TrendScore, int Votes, int DistinctVoterIpBlocks);
}
