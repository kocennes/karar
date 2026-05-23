using System.Security.Cryptography;

namespace Karar.Api.Services;

public sealed class DeviceTokenService
{
    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return $"dt_{Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=')}";
    }
}
