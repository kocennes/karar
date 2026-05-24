using System.Net;
using Karar.Api.Services;

namespace Karar.UnitTests.Security;

public sealed class SubnetBanServiceTests
{
    [Fact]
    public void GetSubnet_ReturnsIpv4Slash24()
    {
        var subnet = SubnetBanService.GetSubnet(IPAddress.Parse("203.0.113.42"));

        Assert.Equal("203.0.113.0/24", subnet);
    }

    [Fact]
    public void GetSubnet_ReturnsIpv4Slash24ForMappedIpv6()
    {
        var subnet = SubnetBanService.GetSubnet(IPAddress.Parse("::ffff:203.0.113.42"));

        Assert.Equal("203.0.113.0/24", subnet);
    }

    [Fact]
    public void GetSubnet_ReturnsIpv6Slash64()
    {
        var subnet = SubnetBanService.GetSubnet(IPAddress.Parse("2001:db8:abcd:1234:1111:2222:3333:4444"));

        Assert.Equal("2001:db8:abcd:1234::/64", subnet);
    }
}
