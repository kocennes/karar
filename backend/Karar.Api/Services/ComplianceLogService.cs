using System.Security.Cryptography;
using System.Text;
using Karar.Api.Data;
using Npgsql;

namespace Karar.Api.Services;

// 5651 sayılı Kanun gereği bağlantı kaydı tutan servis.
// IP ve cihaz bilgileri günlük-tuzlu SHA-256 hash olarak saklanır; raw değer hiçbir zaman yazılmaz.
// Tablo append-only'dır — UPDATE/DELETE triggerla engellenmiştir.
public sealed class ComplianceLogService
{
    private readonly Db _db;
    private readonly string _dailySalt;
    private readonly ILogger<ComplianceLogService> _logger;

    public ComplianceLogService(Db db, IConfiguration configuration, ILogger<ComplianceLogService> logger)
    {
        _db = db;
        _logger = logger;
        // Production: Compliance:DailySalt Secret Manager'dan gelir.
        // Eksikse dummy salt — development ortamı için yeterli.
        _dailySalt = configuration["Compliance:DailySalt"] ?? "dev-compliance-salt";
    }

    // action: 'vote' | 'post_create' | 'comment_create' | 'login' | 'register' | 'report'
    public async Task LogAsync(
        string action,
        string? ip,
        Guid? deviceId,
        Guid? userId,
        Guid? targetId,
        string? targetType,
        object? metadata = null,
        CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.UtcNow.ToString("yyyyMMdd");
            var ipHash = HashValue(ip ?? "unknown", today);
            var deviceHash = deviceId.HasValue ? HashValue(deviceId.Value.ToString(), today) : null;

            await using var connection = await _db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO compliance_logs
                    (action, ip_hash, device_hash, user_id, target_id, target_type, metadata)
                VALUES
                    (@action, @ipHash, @deviceHash, @userId, @targetId, @targetType, @metadata::jsonb)
                """,
                connection
            );
            cmd.Parameters.AddWithValue("action", action);
            cmd.Parameters.AddWithValue("ipHash", ipHash);
            cmd.Parameters.AddWithValue("deviceHash", (object?)deviceHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("userId", (object?)userId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("targetId", (object?)targetId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("targetType", (object?)targetType ?? DBNull.Value);
            cmd.Parameters.AddWithValue("metadata",
                metadata is not null
                    ? System.Text.Json.JsonSerializer.Serialize(metadata)
                    : (object)DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            // Compliance log hatası isteği durdurmamalı — sadece logla.
            _logger.LogError(ex, "Compliance log yazılamadı: {Action}", action);
        }
    }

    private string HashValue(string value, string date)
    {
        var input = $"{value}:{date}:{_dailySalt}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
