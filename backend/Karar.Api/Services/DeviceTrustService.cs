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

        return new DeviceTrustDecision(score, isSuspicious, isSuspicious, reason);
    }

    // Soft-enforce evaluation for non-vote actions (report, create-post).
    // Suspicious devices are flagged and scored, but ShouldQuarantineVote is always false —
    // the request is never blocked on missing or failed integrity signal alone.
    public async Task<DeviceTrustDecision> EvaluateForActionAsync(
        NpgsqlConnection connection,
        Guid deviceId
    )
    {
        var signals = await LoadSignalsAsync(connection, null, deviceId);
        var score = CalculateScore(signals);
        var isSuspicious = score < SuspiciousThreshold || HasSuspiciousBehavior(signals);
        var reason = isSuspicious ? BuildSuspiciousReason(signals) : null;

        await UpsertScoreAsync(connection, null, deviceId, signals, score, isSuspicious, reason);

        return new DeviceTrustDecision(score, isSuspicious, ShouldQuarantineVote: false, reason);
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

        score -= Math.Min(signals.FailedIntegrityCount + signals.InvalidIntegrityCount + signals.ExpiredIntegrityCount, 3) * 0.4;
        score -= Math.Min(signals.MissingIntegrityCount, 3) * 0.1;
        score -= Math.Min(signals.ReportAbuseCount, 3) * 0.2;

        return Math.Clamp(Math.Round(score, 3), 0, 1);
    }

    private static async Task<DeviceTrustSignals> LoadSignalsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid deviceId
    )
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT d.created_at,
                   COALESCE(ts.failed_integrity_count, 0),
                   COALESCE(ts.missing_integrity_count, 0),
                   COALESCE(ts.invalid_integrity_count, 0),
                   COALESCE(ts.expired_integrity_count, 0),
                   COALESCE(ts.report_abuse_count, 0),
                   COUNT(DISTINCT v.post_id)::int,
                   COUNT(*) FILTER (WHERE v.created_at >= NOW() - INTERVAL '1 hour')::int
            FROM devices d
            LEFT JOIN device_trust_scores ts ON ts.device_id = d.id
            LEFT JOIN votes v ON v.device_id = d.id
            WHERE d.id = @deviceId
            GROUP BY d.created_at, ts.failed_integrity_count, ts.missing_integrity_count, ts.invalid_integrity_count, ts.expired_integrity_count, ts.report_abuse_count
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
            reader.GetInt32(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt32(2),
            reader.GetInt32(3),
            reader.GetInt32(4)
        );
    }

    public async Task RecordAttestationSignalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid deviceId,
        IntegrityTokenStatus status
    )
    {
        if (status is IntegrityTokenStatus.Valid or IntegrityTokenStatus.Skipped)
            return;

        var missingIncrement = status == IntegrityTokenStatus.Missing ? 1 : 0;
        var invalidIncrement = status == IntegrityTokenStatus.Invalid ? 1 : 0;
        var expiredIncrement = status == IntegrityTokenStatus.Expired ? 1 : 0;
        var failedIncrement = status is IntegrityTokenStatus.Invalid or IntegrityTokenStatus.Expired ? 1 : 0;

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO device_trust_scores (
                device_id,
                failed_integrity_count,
                missing_integrity_count,
                invalid_integrity_count,
                expired_integrity_count,
                is_suspicious,
                suspicious_reason
            )
            VALUES (
                @deviceId,
                @failedIncrement,
                @missingIncrement,
                @invalidIncrement,
                @expiredIncrement,
                @isSuspicious,
                @reason
            )
            ON CONFLICT (device_id)
            DO UPDATE SET
                failed_integrity_count = device_trust_scores.failed_integrity_count + @failedIncrement,
                missing_integrity_count = device_trust_scores.missing_integrity_count + @missingIncrement,
                invalid_integrity_count = device_trust_scores.invalid_integrity_count + @invalidIncrement,
                expired_integrity_count = device_trust_scores.expired_integrity_count + @expiredIncrement,
                is_suspicious = device_trust_scores.is_suspicious OR @isSuspicious,
                suspicious_reason = @reason,
                updated_at = NOW()
            """,
            connection,
            transaction
        );
        command.Parameters.AddWithValue("deviceId", deviceId);
        command.Parameters.AddWithValue("failedIncrement", failedIncrement);
        command.Parameters.AddWithValue("missingIncrement", missingIncrement);
        command.Parameters.AddWithValue("invalidIncrement", invalidIncrement);
        command.Parameters.AddWithValue("expiredIncrement", expiredIncrement);
        command.Parameters.AddWithValue("isSuspicious", status is IntegrityTokenStatus.Invalid or IntegrityTokenStatus.Expired);
        command.Parameters.AddWithValue("reason", $"attestation_{status.ToString().ToLowerInvariant()}");
        await command.ExecuteNonQueryAsync();
    }

    private static async Task UpsertScoreAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
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
                missing_integrity_count,
                invalid_integrity_count,
                expired_integrity_count,
                report_abuse_count,
                vote_breadth_count,
                is_suspicious,
                is_quarantined,
                suspicious_reason
            )
            VALUES (
                @deviceId,
                @score,
                NOW() - (@deviceAgeSeconds * INTERVAL '1 second'),
                NOW(),
                @failedIntegrityCount,
                @missingIntegrityCount,
                @invalidIntegrityCount,
                @expiredIntegrityCount,
                @reportAbuseCount,
                @voteBreadthCount,
                @isSuspicious,
                @isSuspicious,
                @reason
            )
            ON CONFLICT (device_id)
            DO UPDATE SET
                trust_score = @score,
                last_seen_at = NOW(),
                vote_breadth_count = @voteBreadthCount,
                is_suspicious = @isSuspicious,
                is_quarantined = @isSuspicious,
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
        command.Parameters.AddWithValue("missingIntegrityCount", signals.MissingIntegrityCount);
        command.Parameters.AddWithValue("invalidIntegrityCount", signals.InvalidIntegrityCount);
        command.Parameters.AddWithValue("expiredIntegrityCount", signals.ExpiredIntegrityCount);
        command.Parameters.AddWithValue("reportAbuseCount", signals.ReportAbuseCount);
        command.Parameters.AddWithValue("voteBreadthCount", signals.VoteBreadthCount);
        command.Parameters.AddWithValue("isSuspicious", isSuspicious);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static string BuildSuspiciousReason(DeviceTrustSignals signals)
    {
        if (signals.InvalidIntegrityCount > 0)
            return "attestation_invalid";

        if (signals.ExpiredIntegrityCount > 0)
            return "attestation_expired";

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
    int RecentVoteCount,
    int MissingIntegrityCount = 0,
    int InvalidIntegrityCount = 0,
    int ExpiredIntegrityCount = 0
);

public sealed record DeviceTrustDecision(
    double TrustScore,
    bool IsSuspicious,
    bool ShouldQuarantineVote,
    string? Reason = null
);
