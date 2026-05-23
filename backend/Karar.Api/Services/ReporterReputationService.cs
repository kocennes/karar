using Npgsql;

namespace Karar.Api.Services;

public sealed class ReporterReputationService
{
    // Bayesian weight with a prior of 50% accuracy (α=2, β=4).
    // New / anonymous reporters get weight 1.0. Accurate reporters up to 2.0,
    // chronic false-reporters down to 0.5.
    public static double ComputeWeight(int accurateCount, int totalCount)
    {
        var raw = (accurateCount + 2.0) / (totalCount + 4.0) * 2.0;
        return Math.Clamp(raw, 0.5, 2.0);
    }

    // Called when an admin resolves a report. Updates the reporter's accuracy stats
    // so future reports are weighted accordingly. No-op for anonymous reporters.
    public async Task RecordOutcomeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid reportId,
        bool wasActioned)
    {
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE users
            SET reporter_total_count    = reporter_total_count + 1,
                reporter_accurate_count = reporter_accurate_count + @delta
            WHERE id = (SELECT reporter_user_id FROM reports WHERE id = @reportId)
            """,
            connection,
            transaction);
        cmd.Parameters.AddWithValue("reportId", reportId);
        cmd.Parameters.AddWithValue("delta", wasActioned ? 1 : 0);
        await cmd.ExecuteNonQueryAsync();
    }
}
