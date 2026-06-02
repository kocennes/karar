using Npgsql;
using System.Text.Json;

namespace Karar.Api.Services;

// Inline brigade guard — called at vote time, before transaction commit.
// Detects: ≥5 distinct devices voting on the same post within 10 min
// from the same /24 IP block (voter_ip_block) OR the same fingerprint-prefix cluster (first 8 chars).
// On detection: quarantines all votes in the window from the suspect cluster, inserts admin_alert.
// The vote is still recorded (response unchanged) but marked is_quarantined=TRUE so it is
// excluded from trend score computation.
public sealed class VoteBrigadeGuard
{
    public const int SuppressThreshold = 5;
    public const int WindowMinutes = 10;
    public const int FingerprintPrefixLength = 8;

    public async Task<BrigadeGuardResult> CheckAndSuppressAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid postId,
        string? voterIpBlock
    )
    {
        // --- IP block check ---
        if (voterIpBlock is not null)
        {
            var ipCount = await CountVotesByIpBlockAsync(connection, transaction, postId, voterIpBlock);
            if (ipCount >= SuppressThreshold)
            {
                await QuarantineByIpBlockAsync(connection, transaction, postId, voterIpBlock);
                var alertId = await InsertAdminAlertAsync(
                    connection, transaction, postId, ipCount,
                    ipConcentration: 1.0, detectionKind: "ip_block");
                return new BrigadeGuardResult(true, ipCount, IpConcentration: 1.0, alertId);
            }
        }

        // --- Fingerprint prefix check ---
        var fpResult = await CheckFingerprintClusterAsync(connection, transaction, postId);
        if (fpResult.Count >= SuppressThreshold)
        {
            await QuarantineByFingerprintPrefixAsync(connection, transaction, postId, fpResult.Prefix);
            var alertId = await InsertAdminAlertAsync(
                connection, transaction, postId, fpResult.Count,
                ipConcentration: fpResult.Concentration, detectionKind: "fingerprint_prefix");
            return new BrigadeGuardResult(true, fpResult.Count, fpResult.Concentration, alertId);
        }

        return BrigadeGuardResult.None;
    }

    private static async Task<int> CountVotesByIpBlockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid postId,
        string ipBlock
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            SELECT COUNT(DISTINCT v.device_id)
            FROM votes v
            WHERE v.post_id = @postId
              AND v.voter_ip_block = @ipBlock
              AND v.created_at >= NOW() - (@windowMinutes * INTERVAL '1 minute')
              AND v.is_quarantined = FALSE
            """,
            connection, transaction);
        cmd.Parameters.AddWithValue("postId", postId);
        cmd.Parameters.AddWithValue("ipBlock", ipBlock);
        cmd.Parameters.AddWithValue("windowMinutes", WindowMinutes);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private static async Task<(int Count, string Prefix, double Concentration)> CheckFingerprintClusterAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid postId
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            WITH window_votes AS (
                SELECT v.device_id, LEFT(d.fingerprint, @prefixLen) AS fp_prefix
                FROM votes v
                JOIN devices d ON d.id = v.device_id
                WHERE v.post_id = @postId
                  AND v.created_at >= NOW() - (@windowMinutes * INTERVAL '1 minute')
                  AND v.is_quarantined = FALSE
            ),
            prefix_counts AS (
                SELECT fp_prefix, COUNT(DISTINCT device_id)::int AS cnt
                FROM window_votes
                GROUP BY fp_prefix
            ),
            totals AS (
                SELECT COUNT(DISTINCT device_id)::int AS total FROM window_votes
            )
            SELECT pc.fp_prefix,
                   pc.cnt,
                   pc.cnt::double precision / NULLIF(t.total, 0) AS concentration
            FROM prefix_counts pc
            CROSS JOIN totals t
            ORDER BY pc.cnt DESC
            LIMIT 1
            """,
            connection, transaction);
        cmd.Parameters.AddWithValue("postId", postId);
        cmd.Parameters.AddWithValue("windowMinutes", WindowMinutes);
        cmd.Parameters.AddWithValue("prefixLen", FingerprintPrefixLength);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return (0, string.Empty, 0.0);

        var prefix = reader.GetString(0);
        var count = reader.GetInt32(1);
        var concentration = reader.IsDBNull(2) ? 0.0 : reader.GetDouble(2);
        return (count, prefix, concentration);
    }

    private static async Task QuarantineByIpBlockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid postId,
        string ipBlock
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE votes
            SET is_quarantined = TRUE,
                quarantined     = TRUE
            WHERE post_id = @postId
              AND voter_ip_block = @ipBlock
              AND created_at >= NOW() - (@windowMinutes * INTERVAL '1 minute')
              AND is_quarantined = FALSE
            """,
            connection, transaction);
        cmd.Parameters.AddWithValue("postId", postId);
        cmd.Parameters.AddWithValue("ipBlock", ipBlock);
        cmd.Parameters.AddWithValue("windowMinutes", WindowMinutes);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task QuarantineByFingerprintPrefixAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid postId,
        string fingerprintPrefix
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE votes v
            SET is_quarantined = TRUE,
                quarantined     = TRUE
            FROM devices d
            WHERE v.device_id = d.id
              AND v.post_id = @postId
              AND LEFT(d.fingerprint, @prefixLen) = @fpPrefix
              AND v.created_at >= NOW() - (@windowMinutes * INTERVAL '1 minute')
              AND v.is_quarantined = FALSE
            """,
            connection, transaction);
        cmd.Parameters.AddWithValue("postId", postId);
        cmd.Parameters.AddWithValue("fpPrefix", fingerprintPrefix);
        cmd.Parameters.AddWithValue("prefixLen", FingerprintPrefixLength);
        cmd.Parameters.AddWithValue("windowMinutes", WindowMinutes);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<long> InsertAdminAlertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid postId,
        int deviceCount,
        double ipConcentration,
        string detectionKind
    )
    {
        var payload = JsonSerializer.Serialize(new
        {
            post_id = postId,
            device_count = deviceCount,
            ip_concentration = ipConcentration,
            detection_kind = detectionKind,
            detected_at = DateTimeOffset.UtcNow
        });

        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO admin_alerts (type, payload)
            VALUES ('brigade_inline_suppressed', @payload::jsonb)
            RETURNING id
            """,
            connection, transaction);
        cmd.Parameters.AddWithValue("payload", payload);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync() ?? 0L);
    }
}

public sealed record BrigadeGuardResult(
    bool Detected,
    int DeviceCount,
    double IpConcentration,
    long AlertId = 0
)
{
    public static readonly BrigadeGuardResult None = new(false, 0, 0.0);
}
