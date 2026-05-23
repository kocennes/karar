using Npgsql;

namespace Karar.Api.Services;

public sealed class DeviceTrustService
{
    public const double SuspiciousThreshold = 0.2;

    public async Task<DeviceTrustDecision> EvaluateForVoteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid deviceId
    )
    {
        var signals = await LoadSignalsAsync(connection, transaction, deviceId);
        var score = CalculateScore(signals);
        var isSuspicious = score < SuspiciousThreshold || HasSuspiciousBehavior(signals);
        var reason = isSuspicious ? BuildSuspiciousReason(signals) : null;

        await UpsertScoreAsync(connection, transaction, deviceId, signals, score, isSuspicious, reason);

        return new DeviceTrustDecision(score, isSuspicious, isSuspicious);
    }

    public static double CalculateScore(DeviceTrustSignals signals)
    {
        var score = 0.5;

        if (signals.DeviceAge >= TimeSpan.FromDays(7))
            score += 0.1;

        if (signals.VoteBreadthCount >= 3)
            score += 0.1;

        if (signals.DeviceAge <= TimeSpan.FromHours(1) && signals.RecentVoteCount >= 10)
            score -= 0.2;

        score -= Math.Min(signals.FailedIntegrityCount, 3) * 0.4;
        score -= Math.Min(signals.ReportAbuseCount, 3) * 0.2;

        return Math.Clamp(Math.Round(score, 3), 0, 1);
    }

    private static async Task<DeviceTrustSignals> LoadSignalsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid deviceId
    )
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT d.created_at,
                   COALESCE(ts.failed_integrity_count, 0),
                   COALESCE(ts.report_abuse_count, 0),
                   COUNT(DISTINCT v.post_id)::int,
                   COUNT(*) FILTER (WHERE v.created_at >= NOW() - INTERVAL '1 hour')::int
            FROM devices d
            LEFT JOIN device_trust_scores ts ON ts.device_id = d.id
            LEFT JOIN votes v ON v.device_id = d.id
            WHERE d.id = @deviceId
            GROUP BY d.created_at, ts.failed_integrity_count, ts.report_abuse_count
            """,
            connection,
            transaction
        );
        command.Parameters.AddWithValue("deviceId", deviceId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return new DeviceTrustSignals(TimeSpan.Zero, 0, 0, 0, 0);

        var createdAt = reader.GetFieldValue<DateTimeOffset>(0);
        return new DeviceTrustSignals(
            DateTimeOffset.UtcNow - createdAt,
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4)
        );
    }

    private static async Task UpsertScoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid deviceId,
        DeviceTrustSignals signals,
        double score,
        bool isSuspicious,
        string? reason
    )
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO device_trust_scores (
                device_id,
                trust_score,
                first_seen_at,
                last_seen_at,
                failed_integrity_count,
                report_abuse_count,
                vote_breadth_count,
                is_suspicious,
                suspicious_reason
            )
            VALUES (
                @deviceId,
                @score,
                NOW() - (@deviceAgeSeconds * INTERVAL '1 second'),
                NOW(),
                @failedIntegrityCount,
                @reportAbuseCount,
                @voteBreadthCount,
                @isSuspicious,
                @reason
            )
            ON CONFLICT (device_id)
            DO UPDATE SET
                trust_score = @score,
                last_seen_at = NOW(),
                vote_breadth_count = @voteBreadthCount,
                is_suspicious = @isSuspicious,
                suspicious_reason = @reason,
                updated_at = NOW()
            """,
            connection,
            transaction
        );
        command.Parameters.AddWithValue("deviceId", deviceId);
        command.Parameters.AddWithValue("score", score);
        command.Parameters.AddWithValue("deviceAgeSeconds", Math.Max(0, signals.DeviceAge.TotalSeconds));
        command.Parameters.AddWithValue("failedIntegrityCount", signals.FailedIntegrityCount);
        command.Parameters.AddWithValue("reportAbuseCount", signals.ReportAbuseCount);
        command.Parameters.AddWithValue("voteBreadthCount", signals.VoteBreadthCount);
        command.Parameters.AddWithValue("isSuspicious", isSuspicious);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildSuspiciousReason(DeviceTrustSignals signals)
    {
        if (signals.FailedIntegrityCount > 0)
            return "integrity_failed";

        if (signals.ReportAbuseCount > 0)
            return "report_abuse";

        if (signals.DeviceAge <= TimeSpan.FromHours(1) && signals.RecentVoteCount >= 10)
            return "new_device_vote_burst";

        return "low_trust_score";
    }

    private static bool HasSuspiciousBehavior(DeviceTrustSignals signals) =>
        signals.DeviceAge <= TimeSpan.FromHours(1) && signals.RecentVoteCount >= 10;
}

public sealed record DeviceTrustSignals(
    TimeSpan DeviceAge,
    int FailedIntegrityCount,
    int ReportAbuseCount,
    int VoteBreadthCount,
    int RecentVoteCount
);

public sealed record DeviceTrustDecision(
    double TrustScore,
    bool IsSuspicious,
    bool ShouldQuarantineVote
);
