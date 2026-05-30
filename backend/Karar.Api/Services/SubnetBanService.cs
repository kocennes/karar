using System.Net;
using System.Net.Sockets;
using Karar.Api.Data;
using Npgsql;
using StackExchange.Redis;

namespace Karar.Api.Services;

public sealed class SubnetBanService(
    Db db,
    RedisService redis,
    ILogger<SubnetBanService> logger)
{
    public const int AutoBanThreshold = 10;
    public static readonly TimeSpan AutoBanDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan RejectionWindow = TimeSpan.FromMinutes(5);

    private readonly IDatabase _cache = redis.GetDb();

    public static string? GetSubnet(IPAddress? ip)
    {
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork && bytes.Length == 4)
        {
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && bytes.Length == 16)
        {
            return string.Join(':', Enumerable.Range(0, 4)
                .Select(i => BitConverter.ToUInt16(bytes.Skip(i * 2).Take(2).Reverse().ToArray(), 0).ToString("x"))) + "::/64";
        }

        return ip.ToString();
    }

    private static readonly TimeSpan BanCacheTtl = TimeSpan.FromMinutes(1);
    private static string BanCacheKey(string subnet) => $"ban:subnet:{subnet}";

    public async Task<bool> IsBannedAsync(IPAddress? ip)
    {
        var subnet = GetSubnet(ip);
        if (subnet is null) return false;

        // Loopback and private IPs are never banned (also avoids DB roundtrip in tests)
        if (ip is not null && (IPAddress.IsLoopback(ip) || IsPrivate(ip))) return false;

        // Redis cache: avoid DB hit on every request for the same /24 subnet
        var cacheKey = BanCacheKey(subnet);
        try
        {
            var cached = await _cache.StringGetAsync(cacheKey);
            if (cached.HasValue) return cached == "1";
        }
        catch { /* Redis unavailable: fall through to DB */ }

        // Cache miss: query DB
        bool isBanned;
        try
        {
            await using var connection = await db.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(
                """
                SELECT 1
                FROM banned_subnets
                WHERE subnet = @subnet
                  AND (expires_at IS NULL OR expires_at > NOW())
                LIMIT 1
                """,
                connection
            );
            command.Parameters.AddWithValue("subnet", subnet);
            isBanned = await command.ExecuteScalarAsync() is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // DB unreachable: fail open so we never block legitimate traffic
            logger.LogWarning(ex, "SubnetBanService: DB unreachable for subnet {Subnet}, failing open", subnet);
            return false;
        }

        // Populate cache (best-effort; ignore Redis errors)
        try { await _cache.StringSetAsync(cacheKey, isBanned ? "1" : "0", BanCacheTtl); }
        catch { }

        return isBanned;
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        return ip.AddressFamily == AddressFamily.InterNetwork && (
            b[0] == 10 ||
            (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && b[1] == 168));
    }

    public async Task RecordRateLimitRejectionAsync(IPAddress? ip)
    {
        var subnet = GetSubnet(ip);
        if (subnet is null) return;

        var key = RejectionKey(subnet);
        var count = await _cache.StringIncrementAsync(key);
        if (count == 1)
        {
            await _cache.KeyExpireAsync(key, RejectionWindow);
        }

        if (count < AutoBanThreshold)
        {
            return;
        }

        await AutoBanAsync(subnet);
        await _cache.KeyDeleteAsync(key);
    }

    public async Task ClearRateLimitRejectionsAsync(IPAddress? ip)
    {
        var subnet = GetSubnet(ip);
        if (subnet is null) return;

        await _cache.KeyDeleteAsync(RejectionKey(subnet));
    }

    private async Task AutoBanAsync(string subnet)
    {
        var reason = $"{AutoBanThreshold} consecutive 429 responses";

        await using var connection = await db.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO banned_subnets (subnet, reason, admin_email, expires_at)
            VALUES (@subnet, @reason, 'system:auto-rate-limit', NOW() + (@durationSeconds * INTERVAL '1 second'))
            ON CONFLICT (subnet) DO UPDATE
            SET reason = @reason,
                admin_email = 'system:auto-rate-limit',
                expires_at = NOW() + (@durationSeconds * INTERVAL '1 second')
            """,
            connection
        );
        command.Parameters.AddWithValue("subnet", subnet);
        command.Parameters.AddWithValue("reason", reason);
        command.Parameters.AddWithValue("durationSeconds", (int)AutoBanDuration.TotalSeconds);
        await command.ExecuteNonQueryAsync();

        logger.LogWarning("Subnet temporarily banned after consecutive 429 responses. Subnet={Subnet}", subnet);
    }

    private static string RejectionKey(string subnet) => $"rl:subnet429:{subnet}";
}
