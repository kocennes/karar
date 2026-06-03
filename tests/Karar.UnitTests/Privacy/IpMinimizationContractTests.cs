using FluentAssertions;
using Karar.Api.Common;
using System.Net;

namespace Karar.UnitTests.Privacy;

public sealed class IpMinimizationContractTests
{
    [Fact]
    public void VoteAndReportPersistence_StoresIpBlocksNotRawIpAddresses()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        programText.Should().Contain("voter_ip_block");
        programText.Should().Contain("reporter_ip_block");
        programText.Should().Contain("command.Parameters.AddWithValue(\"ipBlock\", (object?)voterIpBlock ?? DBNull.Value)");
        programText.Should().Contain("command.Parameters.AddWithValue(\"ipBlock\", (object?)GetClientIpBlock(httpRequest) ?? DBNull.Value)");
        programText.Should().NotContain("voter_ip TEXT");
        programText.Should().NotContain("reporter_ip TEXT");
    }

    [Fact]
    public void ClientIpBlockHelper_DoesNotFallbackToFullIpString()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var helperStart = programText.IndexOf("static string? GetClientIpBlock(HttpRequest request)", StringComparison.Ordinal);
        helperStart.Should().BeGreaterThanOrEqualTo(0);
        var helperEnd = programText.IndexOf("static async Task AutoHideReportedTargetAsync", helperStart, StringComparison.Ordinal);
        helperEnd.Should().BeGreaterThan(helperStart);
        var helper = programText[helperStart..helperEnd];

        helper.Should().Contain("IpAddressPrivacy.ToNetworkBlock");
        helper.Should().NotContain("return ip.ToString();");
    }

    [Fact]
    public void IpAddressPrivacy_ConvertsAddressesToNetworkBlocks()
    {
        IpAddressPrivacy.ToNetworkBlock(IPAddress.Parse("203.0.113.42")).Should().Be("203.0.113.0/24");
        IpAddressPrivacy.ToNetworkBlock(IPAddress.Parse("2001:db8:abcd:1200:0000:0000:0000:0042"))
            .Should().Be("2001:db8:abcd:1200::/64");
    }

    [Fact]
    public void SubnetBanService_DoesNotFallbackToRawIpString()
    {
        var serviceText = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "SubnetBanService.cs");

        serviceText.Should().Contain("IpAddressPrivacy.ToNetworkBlock(ip)");
        serviceText.Should().NotContain("return ip.ToString();");
    }

    [Fact]
    public void AdminActionAuditNotes_StoreIpBlockNotRawIp()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var middlewareText = TestRepoPaths.ReadText("backend", "Karar.Api", "Common", "Middleware", "AdminSecurityMiddleware.cs");

        programText.Should().Contain("var ipBlock = IpAddressPrivacy.ToNetworkBlock(httpContext.Connection.RemoteIpAddress) ?? \"unknown\"");
        programText.Should().Contain("$\"IP block: {ipBlock}\"");
        programText.Should().NotContain("$\"IP: {ip}\"");

        middlewareText.Should().Contain("var ipBlock = IpAddressPrivacy.ToNetworkBlock(context.Connection.RemoteIpAddress) ?? \"unknown\"");
        middlewareText.Should().Contain("$\"IP block: {ipBlock}, {method} {path}, HTTP {statusCode}\"");
        middlewareText.Should().NotContain("$\"IP: {ip}, {method} {path}, HTTP {statusCode}\"");
    }

    [Fact]
    public void ComplianceLogs_StoreOnlyHashedIpAndDeviceValues()
    {
        var serviceText = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "ComplianceLogService.cs");
        var migrationText = TestRepoPaths.ReadText("backend", "migrations", "V22__compliance_logs.sql");

        serviceText.Should().Contain("var ipHash = HashValue(ip ?? \"unknown\", today)");
        serviceText.Should().Contain("var deviceHash = deviceId.HasValue ? HashValue(deviceId.Value.ToString(), today) : null");
        serviceText.Should().Contain("@ipHash");
        serviceText.Should().NotContain("@ip,");
        migrationText.Should().Contain("ip_hash");
        migrationText.Should().Contain("device_hash");
        migrationText.Should().NotContain(" ip TEXT");
        migrationText.Should().NotContain("device_id UUID");
    }
}
