using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

public sealed class PoliticalNarrativeClusterJob(
    IConfiguration configuration,
    ILogger<PoliticalNarrativeClusterJob> logger,
    EmailService emailService,
    CategoryThrottleService categoryThrottle,
    ComplianceLogService complianceLog)
    : BackgroundService
{
    private readonly string _connectionString = Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private static readonly TimeSpan AutoThrottleDuration = TimeSpan.FromHours(2);
    private const double SpikeMultiplier = 3.0;
    private const int YoungAccountMaxHours = 72;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("PoliticalNarrativeClusterJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckForVoteAnomaliesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "PoliticalNarrativeClusterJob error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task CheckForVoteAnomaliesAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            WITH recent AS (
                SELECT
                    p.id,
                    p.title,
                    p.category_id,
                    COUNT(v.*)::int AS recent_votes,
                    COUNT(DISTINCT v.device_id)::int AS distinct_voters,
                    COUNT(DISTINCT v.voter_ip_block) FILTER (WHERE v.voter_ip_block IS NOT NULL) AS distinct_ip_blocks,
                    PERCENTILE_CONT(0.5) WITHIN GROUP (
                        ORDER BY EXTRACT(EPOCH FROM (NOW() - d.created_at)) / 3600.0
                    ) AS median_account_age_hours,
                    COUNT(*) FILTER (
                        WHERE d.created_at >= NOW() - INTERVAL '72 hours'
                    )::int AS young_voters
                FROM posts p
                JOIN votes v ON v.post_id = p.id
                JOIN devices d ON d.id = v.device_id
                WHERE v.created_at >= NOW() - INTERVAL '6 hours'
                  AND p.status = 'active'
                GROUP BY p.id, p.title, p.category_id
            ),
            baseline AS (
                SELECT
                    p.id,
                    COUNT(v.*)::double precision / 162.0 AS baseline_hourly
                FROM posts p
                LEFT JOIN votes v ON v.post_id = p.id
                    AND v.created_at >= NOW() - INTERVAL '7 days'
                    AND v.created_at < NOW() - INTERVAL '6 hours'
                GROUP BY p.id
            )
            SELECT
                r.id,
                r.title,
                r.category_id,
                r.recent_votes / 6.0 AS vote_rate_last_6h,
                b.baseline_hourly,
                r.median_account_age_hours,
                r.distinct_voters,
                r.distinct_ip_blocks,
                r.young_voters
            FROM recent r
            JOIN baseline b ON b.id = r.id
            WHERE b.baseline_hourly > 0
              AND r.recent_votes / 6.0 > b.baseline_hourly * @spikeMultiplier
              AND r.median_account_age_hours < @youngAccountMaxHours
            """,
            connection);
        command.Parameters.AddWithValue("spikeMultiplier", SpikeMultiplier);
        command.Parameters.AddWithValue("youngAccountMaxHours", YoungAccountMaxHours);

        var anomalies = new List<VoteAnomaly>();
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                anomalies.Add(new VoteAnomaly(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetDouble(3),
                    reader.GetDouble(4),
                    Convert.ToDouble(reader.GetValue(5)),
                    reader.GetInt32(6),
                    reader.GetInt32(7),
                    reader.GetInt32(8)));
            }
        }

        foreach (var anomaly in anomalies)
        {
            var reason = "brigade_detected";
            await categoryThrottle.SetThrottledUntilAsync(
                anomaly.CategoryId,
                DateTime.UtcNow.Add(AutoThrottleDuration),
                reason);

            logger.LogWarning(
                "POLITICAL_NARRATIVE_ALERT: PostId={PostId}, VoteRate={VoteRate:F2}/h, Baseline={Baseline:F2}/h, MedianAccountAge={Age:F0}h",
                anomaly.PostId,
                anomaly.VoteRateLast6h,
                anomaly.BaselineHourly,
                anomaly.MedianAccountAgeHours);

            _ = complianceLog.LogAsync(
                "brigade_category_throttle",
                ip: null,
                deviceId: null,
                userId: null,
                targetId: anomaly.PostId,
                targetType: "post",
                metadata: new
                {
                    category_id = anomaly.CategoryId,
                    reason,
                    vote_rate_last_6h = anomaly.VoteRateLast6h,
                    baseline_hourly = anomaly.BaselineHourly,
                    median_account_age_hours = anomaly.MedianAccountAgeHours,
                    distinct_voters = anomaly.DistinctVoters,
                    distinct_ip_blocks = anomaly.DistinctIpBlocks,
                    young_voters = anomaly.YoungVoters
                },
                ct);

            await NotifyAdminAsync(anomaly, ct);
        }
    }

    private async Task NotifyAdminAsync(VoteAnomaly anomaly, CancellationToken ct)
    {
        try
        {
            var subject = "[Karar] Brigade vote anomaly";
            var body = $"""
                Coordinated voting anomaly detected.

                post_id: {anomaly.PostId}
                title: {anomaly.Title}
                vote_rate_last_6h: {anomaly.VoteRateLast6h:F2}/hour
                baseline_hourly: {anomaly.BaselineHourly:F2}/hour
                anomaly_multiplier: {anomaly.VoteRateLast6h / anomaly.BaselineHourly:F1}x

                voter_profile:
                  distinct_voters: {anomaly.DistinctVoters}
                  distinct_voter_ip_blocks: {anomaly.DistinctIpBlocks}
                  median_account_age_hours: {anomaly.MedianAccountAgeHours:F0}
                  young_voters_under_72h: {anomaly.YoungVoters}

                category_throttle:
                  category_id: {anomaly.CategoryId}
                  until: {DateTime.UtcNow.Add(AutoThrottleDuration):O}
                  reason: brigade_detected
                """;

            await emailService.SendAdminAlertAsync(subject, body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Admin brigade alert email failed");
        }
    }

    private sealed record VoteAnomaly(
        Guid PostId,
        string Title,
        int CategoryId,
        double VoteRateLast6h,
        double BaselineHourly,
        double MedianAccountAgeHours,
        int DistinctVoters,
        int DistinctIpBlocks,
        int YoungVoters);
}
