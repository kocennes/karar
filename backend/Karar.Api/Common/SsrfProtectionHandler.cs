using System.Net;
using System.Net.Sockets;

namespace Karar.Api.Common;

/// <summary>
/// Prevents Server-Side Request Forgery (SSRF) by blocking internal, loopback,
/// and link-local IP addresses in outgoing HttpClient requests.
/// </summary>
public sealed class SsrfProtectionHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        if (uri is null)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // 1. Hostname is an IP address? Check directly.
        if (IPAddress.TryParse(uri.IdnHost, out var address))
        {
            ValidateAddress(address);
        }
        else
        {
            // 2. Resolve hostname to IP addresses and check all.
            // Note: This doesn't prevent DNS Rebinding but provides a good first layer.
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(uri.IdnHost, cancellationToken);
                foreach (var addr in addresses)
                {
                    ValidateAddress(addr);
                }
            }
            catch (SocketException)
            {
                // Host not found, let the base handler handle the actual failure.
            }
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private static void ValidateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            throw new InvalidOperationException($"SSRF Prevention: Loopback address blocked: {address}");
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            throw new InvalidOperationException($"SSRF Prevention: Internal IPv6 address blocked: {address}");
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            // RFC1918 Private Address Spaces
            // 10.0.0.0/8
            if (bytes[0] == 10)
            {
                throw new InvalidOperationException($"SSRF Prevention: Private address blocked: {address}");
            }

            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            {
                throw new InvalidOperationException($"SSRF Prevention: Private address blocked: {address}");
            }

            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168)
            {
                throw new InvalidOperationException($"SSRF Prevention: Private address blocked: {address}");
            }

            // 169.254.0.0/16 (Link-local)
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                throw new InvalidOperationException($"SSRF Prevention: Link-local address blocked: {address}");
            }
        }
    }
}
