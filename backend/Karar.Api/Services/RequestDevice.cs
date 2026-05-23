using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

public sealed class RequestDevice
{
    private readonly Db _db;

    public RequestDevice(Db db)
    {
        _db = db;
    }

    public async Task<Guid?> TryGetDeviceIdAsync(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("X-Device-Token", out var values))
        {
            return null;
        }

        var token = values.ToString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        await using var connection = await _db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            UPDATE devices
            SET last_seen_at = NOW()
            WHERE device_token = @token AND is_banned = FALSE
            RETURNING id
            """,
            connection
        );
        command.Parameters.AddWithValue("token", token);

        var result = await command.ExecuteScalarAsync();
        return result is Guid id ? id : null;
    }
}
