using System.Net;
using System.Net.Sockets;

namespace Karar.Api.Common;

public static class IpAddressPrivacy
{
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
}
