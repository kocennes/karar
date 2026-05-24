using Karar.Api.Services;

namespace Karar.UnitTests.Security;

public sealed class BruteForceServiceTests
{
    [Fact]
    public void IdentityFor_IncludesIpDeviceAndEndpoint()
    {
        var deviceId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var identity = BruteForceService.IdentityFor("203.0.113.10", "login", deviceId);

        Assert.Equal("203.0.113.10:11111111222233334444555555555555:login", identity);
    }

    [Fact]
    public void IdentityFor_UsesStableFallbackWhenDeviceIsMissing()
    {
        var identity = BruteForceService.IdentityFor("203.0.113.10", "admin-login");

        Assert.Equal("203.0.113.10:unknown-device:admin-login", identity);
    }
}
