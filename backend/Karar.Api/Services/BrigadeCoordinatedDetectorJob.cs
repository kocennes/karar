using Karar.Api.Data;
using Npgsql;
using System.Text.Json;

namespace Karar.Api.Services;

// Koordineli oylama anomalisi dedektörü — 10 dakikada bir çalışır.
// Tespit kriterleri (hepsi aynı anda): 6 dk içinde aynı posta ≥15 cihaz,
// bu cihazların ≥%60'ı aynı /24 IP bloğundan veya aynı fingerprint prefix'i (ilk 8 karakter),
// medyan hesap yaşı < 72 saat, son 24 saatte birden fazla farklı posta aynı yönde oy.
public sealed class BrigadeCoordinatedDetectorJob(
    IConfiguration configuration,
    ILogger<BrigadeCoordinatedDetectorJob> logger)
    : BackgroundService
{
    private readonly string _connectionString = Db.ConvertToKeyValue(
        configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("ConnectionStrings:Postgres is missing."));

    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    public const int MinDevices = 15;
    public const double IpBlockConcentrationThreshold = 0.60;
    public const int YoungAccountMaxHours = 72;
    public const int VoteWindowMinutes = 6;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BrigadeCoordinatedDetectorJob started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DetectAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "BrigadeCoordinatedDetectorJob error");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    internal async Task DetectAsync(CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        var suspects = await FindSuspectClustersAsync(connection, ct);

        foreach (var cluster in suspects)
        {
            await QuarantineVotesAsync(connection, cluster, ct);
            await InsertAdminAlertAsync(connection, cluster, ct);
            logger.LogWarning(
                "BRIGADE_COORDINATED: PostId={PostId} Devices={DeviceCount} IpConcentration={IpConc:P0} MedianAge={Age:F0}h",
                cluster.PostId, cluster.DeviceCount, cluster.IpConcentration, cluster.MedianAccountAgeHours);
        }
    }

    private static async Task<List<BrigadeCluster>> FindSuspectClustersAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        // Step 1: posts with ≥15 distinct devices in 6-min window
        await using var clusterCmd = new NpgsqlCommand(
            """
            WITH window_votes AS (
                SELECT
                    v.post_id,
                    v.device_id,
                    v.vote_type,
                    v.voter_ip_block,
                    d.fingerprint,
                    d.created_at AS device_created_at,
                    EXTRACT(EPOCH FROM (NOW() - d.created_at)) / 3600.0 AS account_age_hours
                FROM votes v
                JOIN devices d ON d.id = v.device_id
                WHERE v.created_at >= NOW() - INTERVAL '6 minutes'
                  AND v.quarantined = FALSE
            ),
            cluster_stats AS (
                SELECT
                    post_id,
                    COUNT(DISTINCT device_id)::int                                    AS device_count,
                    MODE() WITHIN GROUP (ORDER BY vote_type)                          AS dominant_vote_type,
                    PERCENTILE_CONT(0.5) WITHIN GROUP (ORDER BY account_age_hours)    AS median_account_age_hours,
                    COUNT(DISTINCT voter_ip_block)::int                               AS distinct_ip_blocks,
                    COUNT(*)::int                                                      AS total_votes,
                    -- dominant /24 block count
                    MAX(ip_block_count)                                                AS max_ip_block_count,
                    -- dominant fingerprint prefix count (first 8 chars)
                    MAX(fp_prefix_count)                                               AS max_fp_prefix_count
                FROM window_votes
                CROSS JOIN LATERAL (
                    SELECT
                        MAX(c) AS ip_block_count
                    FROM (
                        SELECT COUNT(*) AS c
                        FROM window_votes wv2
                        WHERE wv2.post_id = window_votes.post_id
                          AND wv2.voter_ip_block IS NOT NULL
                          AND wv2.voter_ip_block = window_votes.voter_ip_block
                        GROUP BY wv2.voter_ip_block
                        LIMIT 1
                    ) t
                ) ip_max
                CROSS JOIN LATERAL (
                    SELECT
                        MAX(c) AS fp_prefix_count
                    FROM (
                        SELECT COUNT(*) AS c
                        FROM window_votes wv3
                        WHERE wv3.post_id = window_votes.post_id
                          AND LEFT(wv3.fingerprint, 8) = LEFT(window_votes.fingerprint, 8)
                        GROUP BY LEFT(wv3.fingerprint, 8)
                        LIMIT 1
                    ) t2
                ) fp_max
                GROUP BY post_id
            )
            SELECT
                post_id,
                device_count,
                dominant_vote_type,
                median_account_age_hours,
                distinct_ip_blocks,
                max_ip_block_count::float / NULLIF(device_count, 0) AS ip_concentration,
                max_fp_prefix_count::float / NULLIF(device_count, 0) AS fp_concentration
            FROM cluster_stats
            WHERE device_count >= @minDevices
              AND median_account_age_hours < @youngAccountMaxHours
            """,
            connection);
        clusterCmd.Parameters.AddWithValue("minDevices", MinDevices);
        clusterCmd.Parameters.AddWithValue("youngAccountMaxHours", YoungAccountMaxHours);

        var candidates = new List<BrigadeCluster>();
        await using (var reader = await clusterCmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var ipConc = reader.IsDBNull(5) ? 0.0 : reader.GetDouble(5);
                var fpConc = reader.IsDBNull(6) ? 0.0 : reader.GetDouble(6);
                var concentration = Math.Max(ipConc, fpConc);
                if (concentration < IpBlockConcentrationThreshold)
                    continue;

                candidates.Add(new BrigadeCluster(
                    reader.GetGuid(0),
                    reader.GetInt32(1),
                    reader.GetString(2),
                    reader.GetDouble(3),
                    reader.GetInt32(4),
                    concentration));
            }
        }

        // Step 2: filter to clusters where devices also voted on multiple different posts in same direction in last 24h
        var confirmed = new List<BrigadeCluster>();
        foreach (var candidate in candidates)
        {
            var isCoordinated = await CheckCoordinatedPatternAsync(connection, candidate, ct);
            if (isCoordinated)
                confirmed.Add(candidate);
        }

        return confirmed;
    }

    // Verify: suspect devices voted on multiple different posts in same direction within 24h
    private static async Task<bool> CheckCoordinatedPatternAsync(
        NpgsqlConnection connection,
        BrigadeCluster cluster,
        CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(DISTINCT v2.post_id)
            FROM votes v
            JOIN votes v2 ON v2.device_id = v.device_id
                         AND v2.vote_type = v.vote_type
                         AND v2.created_at >= NOW() - INTERVAL '24 hours'
                         AND v2.post_id != v.post_id
            WHERE v.post_id = @postId
              AND v.created_at >= NOW() - INTERVAL '6 minutes'
              AND v.vote_type = @voteType
            """,
            connection);
        cmd.Parameters.AddWithValue("postId", cluster.PostId);
        cmd.Parameters.AddWithValue("voteType", cluster.DominantVoteType);

        var otherPostCount = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct) ?? 0);
        return otherPostCount >= 1;
    }

    private static async Task QuarantineVotesAsync(
        NpgsqlConnection connection,
        BrigadeCluster cluster,
        CancellationToken ct)
    {
        // Quarantine votes from the suspect cluster (preserve vote, exclude from trend)
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE votes
            SET quarantined = TRUE
            WHERE post_id = @postId
              AND created_at >= NOW() - INTERVAL '6 minutes'
              AND quarantined = FALSE
              AND device_id IN (
                  SELECT device_id FROM votes
                  WHERE post_id = @postId
                    AND created_at >= NOW() - INTERVAL '6 minutes'
              )
            """,
            connection);
        cmd.Parameters.AddWithValue("postId", cluster.PostId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task InsertAdminAlertAsync(
        NpgsqlConnection connection,
        BrigadeCluster cluster,
        CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            post_id = cluster.PostId,
            device_count = cluster.DeviceCount,
            dominant_vote_type = cluster.DominantVoteType,
            median_account_age_hours = cluster.MedianAccountAgeHours,
            distinct_ip_blocks = cluster.DistinctIpBlocks,
            ip_concentration = cluster.IpConcentration,
            detected_at = DateTimeOffset.UtcNow
        });

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO admin_alerts (type, payload)
            VALUES ('brigade_suspected', @payload::jsonb)
            """,
            connection);
        cmd.Parameters.AddWithValue("payload", payload);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal sealed record BrigadeCluster(
        Guid PostId,
        int DeviceCount,
        string DominantVoteType,
        double MedianAccountAgeHours,
        int DistinctIpBlocks,
        double IpConcentration);
}
