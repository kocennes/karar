using System.Net;
using FluentAssertions;
using Karar.Api.Services;
using Microsoft.AspNetCore.Http;

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

    // ── Auto-ban: one attacker must not be able to ban a shared /24 ─────────

    [Fact]
    public async Task TrackRateLimitRejection_SingleIpBurstNeverAutoBansSharedSubnet()
    {
        var (cache, _) = InMemoryRedisDatabase.Create();
        var attacker = IPAddress.Parse("203.0.113.7");

        for (var i = 0; i < 50; i++)
        {
            var subnetToBan = await SubnetBanService.TrackRateLimitRejectionAsync(cache, attacker);
            subnetToBan.Should().BeNull(
                "a single IP behind a CGNAT/campus /24 must not be able to get every neighbour banned (rejection #{0})", i + 1);
        }
    }

    [Fact]
    public async Task TrackRateLimitRejection_BelowDistinctOffenderThresholdDoesNotBan()
    {
        var (cache, _) = InMemoryRedisDatabase.Create();

        for (var host = 1; host < SubnetBanService.DistinctOffendingIpThreshold; host++)
        {
            var ip = IPAddress.Parse($"203.0.113.{host}");
            for (var i = 0; i < SubnetBanService.AutoBanThreshold * 3; i++)
            {
                (await SubnetBanService.TrackRateLimitRejectionAsync(cache, ip)).Should().BeNull();
            }
        }
    }

    [Fact]
    public async Task TrackRateLimitRejection_ManyDistinctOffendingIpsAutoBanSubnet()
    {
        var (cache, _) = InMemoryRedisDatabase.Create();
        string? subnetToBan = null;

        for (var host = 1; host <= SubnetBanService.DistinctOffendingIpThreshold; host++)
        {
            var ip = IPAddress.Parse($"203.0.113.{host}");
            for (var i = 0; i < SubnetBanService.AutoBanThreshold; i++)
            {
                subnetToBan = await SubnetBanService.TrackRateLimitRejectionAsync(cache, ip);
            }
        }

        subnetToBan.Should().Be("203.0.113.0/24");
    }

    [Fact]
    public async Task TrackRateLimitRejection_DoesNotStoreRawIpInRedisKeys()
    {
        var (cache, state) = InMemoryRedisDatabase.Create();

        for (var i = 0; i < SubnetBanService.AutoBanThreshold; i++)
        {
            await SubnetBanService.TrackRateLimitRejectionAsync(cache, IPAddress.Parse("203.0.113.77"));
        }

        state.Keys.Should().NotBeEmpty();
        state.Keys.Should().NotContain(k => k.Contains("203.0.113.77"));
    }

    [Fact]
    public void IsLimiterRejection_IgnoresBusiness429Responses()
    {
        var context = new DefaultHttpContext();
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;

        SubnetBanService.IsLimiterRejection(context).Should().BeFalse(
            "endpoint quotas such as SUBNET_RATE_LIMIT or RATE_LIMIT_COMMENTS are not flood signals");

        SubnetBanService.MarkLimiterRejection(context);
        SubnetBanService.IsLimiterRejection(context).Should().BeTrue();
    }

    [Fact]
    public void SubnetBanMiddleware_CountsOnlyLimiterRejections()
    {
        var middleware = Karar.UnitTests.TestRepoPaths.ReadText(
            "backend", "Karar.Api", "Common", "Middleware", "SubnetBanMiddleware.cs");
        var distributed = Karar.UnitTests.TestRepoPaths.ReadText(
            "backend", "Karar.Api", "Common", "Middleware", "DistributedRateLimitMiddleware.cs");
        var program = Karar.UnitTests.TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        middleware.Should().Contain("SubnetBanService.IsLimiterRejection(context)");
        middleware.Should().NotContain("if (context.Response.StatusCode == StatusCodes.Status429TooManyRequests)",
            "any 429 (including endpoint quotas) must not feed the subnet auto-ban counter");
        distributed.Should().Contain("SubnetBanService.MarkLimiterRejection(context)");

        var onRejected = program[program.IndexOf("options.OnRejected", StringComparison.Ordinal)..];
        onRejected = onRejected[..onRejected.IndexOf("});", StringComparison.Ordinal)];
        onRejected.Should().Contain("SubnetBanService.MarkLimiterRejection(ctx.HttpContext)");
    }

    // ── Device registration budget ─────────────────────────────────────────

    [Fact]
    public async Task DeviceRegistration_SingleIpCannotExhaustSubnetBudget()
    {
        var (cache, _) = InMemoryRedisDatabase.Create();
        var attacker = IPAddress.Parse("203.0.113.7");

        for (var i = 0; i < 50; i++)
        {
            await SubnetBanService.TryConsumeDeviceRegistrationAsync(cache, attacker);
        }

        (await SubnetBanService.TryConsumeDeviceRegistrationAsync(cache, IPAddress.Parse("203.0.113.8")))
            .Should().BeTrue("a neighbour on the same carrier /24 must still be able to install the app");
    }

    [Fact]
    public async Task DeviceRegistration_PerIpLimitStillApplies()
    {
        var (cache, _) = InMemoryRedisDatabase.Create();
        var ip = IPAddress.Parse("203.0.113.7");

        for (var i = 0; i < SubnetBanService.DeviceRegistrationsPerIpPerHour; i++)
        {
            (await SubnetBanService.TryConsumeDeviceRegistrationAsync(cache, ip)).Should().BeTrue();
        }

        (await SubnetBanService.TryConsumeDeviceRegistrationAsync(cache, ip)).Should().BeFalse();
    }

    [Fact]
    public async Task DeviceRegistration_SubnetCapStillAppliesAcrossManyIps()
    {
        var (cache, _) = InMemoryRedisDatabase.Create();
        var allowed = 0;

        for (var host = 1; host <= 200; host++)
        {
            if (await SubnetBanService.TryConsumeDeviceRegistrationAsync(cache, IPAddress.Parse($"203.0.113.{host}")))
            {
                allowed++;
            }
        }

        allowed.Should().Be(SubnetBanService.DeviceRegistrationsPerSubnetPerHour);
    }
}
