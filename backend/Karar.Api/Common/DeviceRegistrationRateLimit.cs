using System.Net;
using System.Net.Sockets;
using StackExchange.Redis;

namespace Karar.Api.Common;

/// <summary>
/// Saatlik cihaz kaydı (anonim kimlik üretimi) limitleri — tek uygulama (sec/05 + sec/07).
/// Kovalar sırayla sayılır; ilk dolan kovada kayıt reddedilir ve sonraki kovalara dokunulmaz:
/// 1) IP başına 5 (özetlenmiş IP; ham IP Redis'e yazılmaz). İlk sırada olduğundan tek bir IP,
///    CGNAT/kampüs/ofis ağındaki komşularının ağ kotasını tüketemez (sec/07).
/// 2) IPv4: /24 başına 30.
///    IPv6: /64 başına 5, /56 başına 20, /48 başına 50 — tek bir abone /56 (256 × /64) veya /48
///    (65.536 × /64) içindeki /64'leri döndürerek sınırsız cihaz üretip oy veremesin diye (sec/05).
/// </summary>
public static class DeviceRegistrationRateLimit
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public const int PerIpLimit = 5;
    public const int PerIpv4Slash24Limit = 30;
    public const int PerIpv6Slash64Limit = 5;
    public const int PerIpv6Slash56Limit = 20;
    public const int PerIpv6Slash48Limit = 50;

    public static IReadOnlyList<DeviceRegistrationBucket> GetBuckets(IPAddress? ip)
    {
        var block = IpAddressPrivacy.ToNetworkBlock(ip);
        var ipKey = IpAddressPrivacy.ToHashedAddressKey(ip);
        if (block is null || ipKey is null)
        {
            return [];
        }

        var buckets = new List<DeviceRegistrationBucket>
        {
            new($"device_reg:ip:{ipKey}", PerIpLimit)
        };

        if (ip is { AddressFamily: AddressFamily.InterNetworkV6, IsIPv4MappedToIPv6: false })
        {
            buckets.Add(new($"device_reg:subnet:{block}", PerIpv6Slash64Limit));
            buckets.Add(new($"device_reg:v6p56:{IpAddressPrivacy.ToNetworkBlock(ip, 56)}", PerIpv6Slash56Limit));
            buckets.Add(new($"device_reg:v6p48:{IpAddressPrivacy.ToNetworkBlock(ip, 48)}", PerIpv6Slash48Limit));
        }
        else
        {
            buckets.Add(new($"device_reg:subnet:{block}", PerIpv4Slash24Limit));
        }

        return buckets;
    }

    /// <summary>
    /// Kaydı bütçeye işler. Kovalar sırayla artırılır; bir kova limiti aşarsa false döner ve
    /// sonraki (daha geniş) kovalar artırılmaz.
    /// </summary>
    public static async Task<bool> TryConsumeAsync(IDatabase cache, IPAddress? ip)
    {
        foreach (var bucket in GetBuckets(ip))
        {
            var count = await cache.StringIncrementAsync(bucket.Key);
            if (count == 1)
            {
                await cache.KeyExpireAsync(bucket.Key, Window);
            }

            if (count > bucket.Limit)
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record DeviceRegistrationBucket(string Key, int Limit);
