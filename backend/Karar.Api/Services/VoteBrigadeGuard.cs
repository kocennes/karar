using Npgsql;
using System.Text.Json;

namespace Karar.Api.Services;

// Inline brigade guard — called at vote time, before transaction commit.
// Detects: >5 distinct devices voting on the same post within 10 min
// from the same /24 IP block (voter_ip_block, server-observed).
// On detection: quarantines all votes in the window from the suspect cluster, inserts admin_alert.
// The vote is still recorded (response unchanged) but marked is_quarantined=TRUE so it is
// excluded from trend score computation.
// Device fingerprints are deliberately NOT a signal here: the value is client-supplied, every
// official client sends the same constant per platform (so a prefix "cluster" is simply all
// Android/iOS/web users) and a scripted client can randomise it at will.
public sealed class VoteBrigadeGuard
{
    public const int SuppressThreshold = 5;
    public const int WindowMinutes = 10;

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
            if (ipCount > SuppressThreshold)
            {
                await SuppressByIpBlockAsync(connection, transaction, postId, voterIpBlock);
                var alertId = await InsertAdminAlertAsync(
                    connection, transaction, postId, ipCount,
                    ipConcentration: 1.0, detectionKind: "ip_block");
                return new BrigadeGuardResult(true, ipCount, IpConcentration: 1.0, alertId);
            }
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
            """,
            connection, transaction);
        cmd.Parameters.AddWithValue("postId", postId);
        cmd.Parameters.AddWithValue("ipBlock", ipBlock);
        cmd.Parameters.AddWithValue("windowMinutes", WindowMinutes);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync() ?? 0);
    }

    private static async Task SuppressByIpBlockAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid postId,
        string ipBlock
    )
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE votes
            SET is_suppressed = TRUE,
                suppression_reason = 'brigade_ip_block',
                suppressed_at = NOW(),
                is_quarantined = TRUE,
                quarantined = TRUE
            WHERE post_id = @postId
              AND voter_ip_block = @ipBlock
              AND created_at >= NOW() - (@windowMinutes * INTERVAL '1 minute')
              AND is_suppressed = FALSE
            """,
            connection, transaction);
        cmd.Parameters.AddWithValue("postId", postId);
        cmd.Parameters.AddWithValue("ipBlock", ipBlock);
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
