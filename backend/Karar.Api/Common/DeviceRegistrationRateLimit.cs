using System.Net;
using System.Net.Sockets;

namespace Karar.Api.Common;

/// <summary>
/// Saatlik cihaz kaydı (anonim kimlik üretimi) limitleri. Her istek, aşağıdaki kovaların HEPSİNE sayılır
/// ve herhangi biri dolduğunda kayıt reddedilir:
/// - IPv4: /24 başına 5 (mevcut anahtar korunur),
/// - IPv6: /64 başına 5, /56 başına 20, /48 başına 50 — tek bir abone /56 (256 × /64) veya /48
///   (65.536 × /64) içindeki /64'leri döndürerek sınırsız cihaz üretip oy veremesin diye.
/// </summary>
public static class DeviceRegistrationRateLimit
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(1);

    public const int PerBlockLimit = 5;
    public const int PerIpv6Slash56Limit = 20;
    public const int PerIpv6Slash48Limit = 50;

    public static IReadOnlyList<DeviceRegistrationBucket> GetBuckets(IPAddress? ip)
    {
        var block = IpAddressPrivacy.ToNetworkBlock(ip);
        if (block is null)
        {
            return [];
        }

        var buckets = new List<DeviceRegistrationBucket>
        {
            new($"device_reg:subnet:{block}", PerBlockLimit)
        };

        if (ip is { AddressFamily: AddressFamily.InterNetworkV6, IsIPv4MappedToIPv6: false })
        {
            buckets.Add(new($"device_reg:v6p56:{IpAddressPrivacy.ToNetworkBlock(ip, 56)}", PerIpv6Slash56Limit));
            buckets.Add(new($"device_reg:v6p48:{IpAddressPrivacy.ToNetworkBlock(ip, 48)}", PerIpv6Slash48Limit));
        }

        return buckets;
    }
}

public sealed record DeviceRegistrationBucket(string Key, int Limit);
