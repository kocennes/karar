using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Admin;

public sealed class OperationsAnalyticsEndpointTests
{
    [Fact]
    public void OperationsEndpoint_RequiresAdmin()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/operations\"",
            "// ── 2FA Yedek Kodları");

        block.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
    }

    [Fact]
    public void OperationsEndpoint_ReturnsSloSnapshot()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/operations\"",
            "// ── 2FA Yedek Kodları");

        block.Should().Contain("sloMetrics.GetSnapshot()");
        block.Should().Contain("slo = new");
        block.Should().Contain("sloSnapshot.Status");
        block.Should().Contain("sloSnapshot.Checks");
        block.Should().Contain("sloSnapshot.BurnRatePolicies");
        block.Should().Contain("windowSeconds");
    }

    [Fact]
    public void OperationsEndpoint_ReturnsCacheMetrics()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/operations\"",
            "// ── 2FA Yedek Kodları");

        block.Should().Contain("redis.GetCacheMetricsAsync()");
        block.Should().Contain("cache = new");
        block.Should().Contain("hitRatePercent");
        block.Should().Contain("targetPercent");
        block.Should().Contain("isHealthy");
    }

    [Fact]
    public void OperationsEndpoint_ReturnsDeployInfo()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/operations\"",
            "// ── 2FA Yedek Kodları");

        block.Should().Contain("deploy = new");
        block.Should().Contain("commitSha");
        block.Should().Contain("deployedAt");
        block.Should().Contain("environment.EnvironmentName");
    }

    [Fact]
    public void OperationsEndpoint_ReturnsBackgroundJobList()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/operations\"",
            "// ── 2FA Yedek Kodları");

        block.Should().Contain("backgroundJobs");
        block.Should().Contain("TrendScoreUpdater");
        block.Should().Contain("NotificationDispatcher");
        block.Should().Contain("PostDistributionJob");
    }

    [Fact]
    public void OperationsEndpoint_IsDocumented()
    {
        if (!TestRepoPaths.TryReadText(out var apiDocs, "docs", "api.md")) return;

        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/operations`");
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker '{startMarker}' should exist");
        end.Should().BeGreaterThan(start, $"end marker '{endMarker}' should exist after start");

        return text[start..end];
    }
}
