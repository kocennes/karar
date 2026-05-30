using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Admin;

public sealed class NorthStarAnalyticsEndpointTests
{
    [Fact]
    public void NorthStarAnalyticsEndpoint_AggregatesCompletedJudgmentLoops()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/north-star\"",
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
        endpointBlock.Should().Contain("Math.Clamp(days, 1, 90)");
        endpointBlock.Should().Contain("FROM discover_events");
        endpointBlock.Should().Contain("FROM growth_events");
        endpointBlock.Should().Contain("FROM notification_events");
        endpointBlock.Should().Contain("weeklyCompletedJudgmentLoops");
        endpointBlock.Should().Contain("backendVerdictViewedProxy");
        endpointBlock.Should().Contain("discoverVotes + shareLandingCompletedJudgment");
        endpointBlock.Should().Contain("completionRate");
    }

    [Fact]
    public void NorthStarAnalyticsEndpoint_UsesMeaningfulDwellAndSourceBreakdown()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/north-star\"",
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"");

        endpointBlock.Should().Contain("COALESCE(dwell_seconds, 0) >= 15",
            "north-star loop must distinguish meaningful dwell from accidental impressions");
        endpointBlock.Should().Contain("discoverToCompletedJudgment");
        endpointBlock.Should().Contain("shareToCompletedJudgment");
        endpointBlock.Should().Contain("notificationToCompletedJudgment");
        endpointBlock.Should().Contain("firebaseVerdictViewedExportRequired",
            "endpoint must be explicit that backend values are a proxy until Firebase verdict_viewed export is connected");
        endpointBlock.Should().Contain("backendProxyUsesVoteAsVerdictViewed");
    }

    [Fact]
    public void NorthStarAnalyticsEndpoint_IsDocumented()
    {
        if (!TestRepoPaths.TryReadText(out var apiDocs, "docs", "api.md")) return;

        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/north-star`",
            "admin analytics endpoint inventory must include the north-star report");
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }
}
