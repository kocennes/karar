using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace Karar.Api.Common;

public static class IpAddressPrivacy
{
    /// <summary>
    /// Ham IP Redis'e yazılmaz (IP minimizasyonu): IPv4-mapped adresler IPv4'e indirgenip
    /// kısaltılmış SHA-256 özeti (32 hex karakter) döndürülür.
    /// </summary>
    public static string? ToHashedAddressKey(IPAddress? ip)
    {
        if (ip is null) return null;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return Convert.ToHexString(SHA256.HashData(ip.GetAddressBytes()), 0, 16).ToLowerInvariant();
    }

    public static string? ToNetworkBlock(IPAddress? ip)
    {
        if (ip is null)
        {
            return null;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

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

        return null;
    }

    /// <summary>
    /// IPv6 adresini daha geniş bir ağ bloğuna indirger (ör. /56, /48); tek bir abonenin elindeki /64'leri
    /// gruplamak için kullanılır. IPv4 (ve IPv4-mapped) adresler için /24 bloğunu döndürür.
    /// </summary>
    public static string? ToNetworkBlock(IPAddress? ip, int ipv6PrefixBits)
    {
        if (ipv6PrefixBits is < 16 or > 64 || ipv6PrefixBits % 8 != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ipv6PrefixBits), "IPv6 prefix must be a multiple of 8 between 16 and 64.");
        }

        if (ip is null || ip.IsIPv4MappedToIPv6 || ip.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return ToNetworkBlock(ip);
        }

        var bytes = ip.GetAddressBytes();
        var prefixBytes = ipv6PrefixBits / 8;
        Array.Clear(bytes, prefixBytes, bytes.Length - prefixBytes);
        return string.Join(':', Enumerable.Range(0, (prefixBytes + 1) / 2)
            .Select(i => ((bytes[i * 2] << 8) | bytes[i * 2 + 1]).ToString("x"))) + $"::/{ipv6PrefixBits}";
    }
}
