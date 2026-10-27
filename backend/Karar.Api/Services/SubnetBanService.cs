using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Karar.Api.Common;
using Karar.Api.Data;
using Npgsql;
using StackExchange.Redis;

namespace Karar.Api.Services;

public sealed class SubnetBanService(
    Db db,
    RedisService redis,
    ILogger<SubnetBanService> logger)
{
    // Bir IP, pencere içinde bu kadar limiter reddi alırsa "suçlu IP" sayılır.
    public const int AutoBanThreshold = 10;
    // /24 (IPv6: /64) ancak bu kadar FARKLI suçlu IP görülürse yasaklanır; tek saldırgan
    // CGNAT/kampüs/ofis ağındaki herkesi yasaklatamaz.
    public const int DistinctOffendingIpThreshold = 5;
    public static readonly TimeSpan AutoBanDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan RejectionWindow = TimeSpan.FromMinutes(5);

    // Cihaz kaydı: önce IP başına, sonra /24 başına saatlik bütçe.
    public const int DeviceRegistrationsPerIpPerHour = 5;
    public const int DeviceRegistrationsPerSubnetPerHour = 30;
    private static readonly TimeSpan DeviceRegistrationWindow = TimeSpan.FromHours(1);

    private const string LimiterRejectionItemKey = "Karar.SubnetBan.LimiterRejection";

    // Yalnızca rate limiter (global/policy/distributed) retleri auto-ban sayacını besler;
    // endpoint kotaları (SUBNET_RATE_LIMIT, RATE_LIMIT_COMMENTS, ACCOUNT_LOCKED ...) beslemez.
    public static void MarkLimiterRejection(HttpContext context) =>
        context.Items[LimiterRejectionItemKey] = true;

    public static bool IsLimiterRejection(HttpContext context) =>
        context.Response.StatusCode == StatusCodes.Status429TooManyRequests
        && context.Items.ContainsKey(LimiterRejectionItemKey);

    private readonly IDatabase _cache = redis.GetDb();

    public static string? GetSubnet(IPAddress? ip)
    {
        return IpAddressPrivacy.ToNetworkBlock(ip);
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
        var subnetToBan = await TrackRateLimitRejectionAsync(_cache, ip);
        if (subnetToBan is not null)
        {
            await AutoBanAsync(subnetToBan);
        }
    }

    // Returns the subnet that must be auto-banned, or null.
    internal static async Task<string?> TrackRateLimitRejectionAsync(IDatabase cache, IPAddress? ip)
    {
        var subnet = GetSubnet(ip);
        var ipKey = IpKey(ip);
        if (subnet is null || ipKey is null) return null;

        var key = RejectionKey(ipKey);
        var count = await cache.StringIncrementAsync(key);
        if (count == 1)
        {
            await cache.KeyExpireAsync(key, RejectionWindow);
        }

        if (count < AutoBanThreshold)
        {
            return null;
        }

        var offendersKey = OffendingIpsKey(subnet);
        await cache.SetAddAsync(offendersKey, ipKey);
        var distinctOffenders = await cache.SetLengthAsync(offendersKey);
        if (distinctOffenders == 1)
        {
            await cache.KeyExpireAsync(offendersKey, RejectionWindow);
        }

        if (distinctOffenders < DistinctOffendingIpThreshold)
        {
            return null;
        }

        await cache.KeyDeleteAsync(offendersKey);
        return subnet;
    }

    public static async Task<bool> TryConsumeDeviceRegistrationAsync(IDatabase cache, IPAddress? ip)
    {
        var subnet = GetSubnet(ip);
        var ipKey = IpKey(ip);
        if (subnet is null || ipKey is null) return true;

        // IP bütçesi aşıldıysa /24 sayacına dokunma: tek IP ağın kotasını tüketemez.
        if (!await IncrementWithinLimitAsync(cache, $"device_reg:ip:{ipKey}", DeviceRegistrationsPerIpPerHour))
        {
            return false;
        }

        return await IncrementWithinLimitAsync(cache, $"device_reg:subnet:{subnet}", DeviceRegistrationsPerSubnetPerHour);
    }

    private static async Task<bool> IncrementWithinLimitAsync(IDatabase cache, string key, int limit)
    {
        var count = await cache.StringIncrementAsync(key);
        if (count == 1)
        {
            await cache.KeyExpireAsync(key, DeviceRegistrationWindow);
        }
        return count <= limit;
    }

    public async Task ClearRateLimitRejectionsAsync(IPAddress? ip)
    {
        var ipKey = IpKey(ip);
        if (ipKey is null) return;

        await _cache.KeyDeleteAsync(RejectionKey(ipKey));
    }

    private async Task AutoBanAsync(string subnet)
    {
        var reason = $"{DistinctOffendingIpThreshold}+ IPs with {AutoBanThreshold} rate-limit rejections";

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

    private static string RejectionKey(string ipKey) => $"rl:ip429:{ipKey}";
    private static string OffendingIpsKey(string subnet) => $"rl:subnet429ips:{subnet}";

    // Ham IP Redis'e yazılmaz (IP minimizasyonu): kısaltılmış SHA-256 özeti kullanılır.
    private static string? IpKey(IPAddress? ip)
    {
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return Convert.ToHexString(SHA256.HashData(ip.GetAddressBytes()), 0, 16).ToLowerInvariant();
    }
}
