using Karar.Api.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Karar.Api.Services;

public sealed class PostDistributionJob(
    Db db,
    ILogger<PostDistributionJob> logger
) : BackgroundService
{
    // Stage 1 -> Stage 2: 10 min elapsed + >=10 votes from UCB exploration slots
    //                      OR 30 min elapsed (time-out promotion)
    // Stage 2 -> Stage 3: trend_score > 3.0 OR total votes > 30
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

        // Stage 1 → Stage 2:
        // Posts older than 10 min with >=10 votes (≈20% of 50 sampled users), OR older than 30 min (timeout)
        await using var stage1Cmd = new NpgsqlCommand(
            """
            UPDATE posts
            SET distribution_stage = 2, updated_at = NOW()
            WHERE distribution_stage = 1
              AND status = 'active'
              AND (
                  (created_at <= NOW() - INTERVAL '10 minutes' AND vote_count_hakli + vote_count_haksiz >= 10)
                  OR created_at <= NOW() - INTERVAL '30 minutes'
              )
            """,
            connection
        );
        var promoted1 = await stage1Cmd.ExecuteNonQueryAsync(ct);
        if (promoted1 > 0)
            logger.LogInformation("[PostDistribution] {Count} posts promoted: Stage 1 -> Stage 2", promoted1);

        // Stage 2 → Stage 3:
        // Posts with trend_score > 3.0 or total votes > 30 AND ≥3 distinct voter IP blocks (geo diversity)
        await using var stage2Cmd = new NpgsqlCommand(
            """
            UPDATE posts
            SET distribution_stage = 3, updated_at = NOW()
            WHERE distribution_stage = 2
              AND status = 'active'
              AND (trend_score > 3.0 OR vote_count_hakli + vote_count_haksiz > 30)
              AND (
                  SELECT COUNT(DISTINCT voter_ip_block)
                  FROM votes
                  WHERE post_id = posts.id AND voter_ip_block IS NOT NULL
              ) >= 3
            """,
            connection
        );
        var promoted2 = await stage2Cmd.ExecuteNonQueryAsync(ct);
        if (promoted2 > 0)
            logger.LogInformation("[PostDistribution] {Count} posts promoted: Stage 2 -> Stage 3", promoted2);

        // Cold-start rescue:
        // Posts that have been at stage 2 for 2+ hours with ≤3 votes are organically invisible.
        // Promote them to stage 3 so they appear in the main feed and get a fair chance.
        await using var coldStartCmd = new NpgsqlCommand(
            """
            UPDATE posts
            SET distribution_stage = 3, updated_at = NOW()
            WHERE distribution_stage = 2
              AND status = 'active'
              AND created_at <= NOW() - INTERVAL '2 hours'
              AND vote_count_hakli + vote_count_haksiz <= 3
            """,
            connection
        );
        var rescued = await coldStartCmd.ExecuteNonQueryAsync(ct);
        if (rescued > 0)
            logger.LogInformation("[PostDistribution] {Count} posts rescued via cold-start: Stage 2 -> Stage 3", rescued);
    }
}
