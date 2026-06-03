using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

public sealed class AffinityService(Db db)
{
    private const double VoteDelta = 0.1;
    private const double CommentDelta = 0.3;
    private const double SaveDelta = 0.5;
    private const double DecayFactor = 0.9;
    private const double MaxScore = 10.0;

    // ── User-based (giriş yapmış kullanıcı) ──────────────────────────────────

    public async Task RecordVoteAsync(Guid userId, int categoryId) =>
        await IncrementAsync(userId, categoryId, VoteDelta);

    public async Task RecordCommentAsync(Guid userId, int categoryId) =>
        await IncrementAsync(userId, categoryId, CommentDelta);

    public async Task RecordSaveAsync(Guid userId, int categoryId) =>
        await IncrementAsync(userId, categoryId, SaveDelta);

    private async Task IncrementAsync(Guid userId, int categoryId, double delta)
    {
        try
        {
            await using var connection = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO user_category_affinity (user_id, category_id, score)
                VALUES (@userId, @categoryId, @delta)
                ON CONFLICT (user_id, category_id)
                DO UPDATE SET
                    score = LEAST(@max, user_category_affinity.score + @delta),
                    updated_at = NOW()
                """,
                connection
            );
            cmd.Parameters.AddWithValue("userId", userId);
            cmd.Parameters.AddWithValue("categoryId", categoryId);
            cmd.Parameters.AddWithValue("delta", delta);
            cmd.Parameters.AddWithValue("max", MaxScore);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    public async Task ApplyWeeklyDecayAsync()
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE user_category_affinity
            SET score = score * @decay, updated_at = NOW()
            WHERE score > 0.01
            """,
            connection
        );
        cmd.Parameters.AddWithValue("decay", DecayFactor);
        await cmd.ExecuteNonQueryAsync();
    }

    // ── Device-based (anonim cihaz — device_category_affinity tablosu) ───────

    public async Task RecordVoteByDeviceAsync(Guid deviceId, int categoryId) =>
        await IncrementDeviceAsync(deviceId, categoryId, VoteDelta);

    public async Task RecordCommentByDeviceAsync(Guid deviceId, int categoryId) =>
        await IncrementDeviceAsync(deviceId, categoryId, CommentDelta);

    public async Task RecordSaveByDeviceAsync(Guid deviceId, int categoryId) =>
        await IncrementDeviceAsync(deviceId, categoryId, SaveDelta);

    private async Task IncrementDeviceAsync(Guid deviceId, int categoryId, double delta)
    {
        try
        {
            await using var connection = await db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO device_category_affinity (device_id, category_id, score)
                VALUES (@deviceId, @categoryId, @delta)
                ON CONFLICT (device_id, category_id)
                DO UPDATE SET
                    score = LEAST(@max, device_category_affinity.score + @delta),
                    updated_at = NOW()
                """,
                connection
            );
            cmd.Parameters.AddWithValue("deviceId", deviceId);
            cmd.Parameters.AddWithValue("categoryId", categoryId);
            cmd.Parameters.AddWithValue("delta", delta);
            cmd.Parameters.AddWithValue("max", MaxScore);
            await cmd.ExecuteNonQueryAsync();
        }
        catch { }
    }

    public async Task ApplyWeeklyDeviceDecayAsync()
    {
        await using var connection = await db.OpenConnectionAsync();
        await using var cmd = new NpgsqlCommand(
            """
            UPDATE device_category_affinity
            SET score = score * @decay, updated_at = NOW()
            WHERE score > 0.01
            """,
            connection
        );
        cmd.Parameters.AddWithValue("decay", DecayFactor);
        await cmd.ExecuteNonQueryAsync();
    }
}
