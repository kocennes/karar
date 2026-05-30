using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Admin;

public sealed class JudgmentLoopSourceTrackingTests
{
    [Fact]
    public void V45Migration_AddsFeedAndSearchEventTypes()
    {
        var sql = TestRepoPaths.ReadText("backend", "migrations", "V45__judgment_loop_feed_search_sources.sql");

        sql.Should().Contain("ALTER TABLE growth_events");
        sql.Should().Contain("'feed_completed_judgment'",
            "feed source must be tracked to measure judgment loops originating from the main feed");
        sql.Should().Contain("'search_completed_judgment'",
            "search source must be tracked to measure judgment loops originating from search results");
        sql.Should().Contain("idx_growth_events_feed_judgment");
        sql.Should().Contain("idx_growth_events_search_judgment");
    }

    [Fact]
    public void GrowthEventsIngestion_AcceptsFeedAndSearchCompletedJudgment()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/growth-events\"",
            "app.MapGet(\"/api/v1/admin/analytics/growth\"");

        endpointBlock.Should().Contain("feed_completed_judgment",
            "growth-events endpoint must accept feed source attribution events");
        endpointBlock.Should().Contain("search_completed_judgment",
            "growth-events endpoint must accept search source attribution events");
    }

    [Fact]
    public void NorthStarEndpoint_IncludesFeedAndSearchSourceBreakdown()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/north-star\"",
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"");

        endpointBlock.Should().Contain("feed_completed_judgment",
            "north-star endpoint must query feed judgment completions from growth_events");
        endpointBlock.Should().Contain("search_completed_judgment",
            "north-star endpoint must query search judgment completions from growth_events");
        endpointBlock.Should().Contain("feedCompletedJudgments",
            "north-star endpoint must expose feed completed judgment count");
        endpointBlock.Should().Contain("searchCompletedJudgments",
            "north-star endpoint must expose search completed judgment count");
        endpointBlock.Should().Contain("sourceBreakdown",
            "north-star endpoint must return a sourceBreakdown object for all 5 sources");
    }

    [Fact]
    public void NorthStarEndpoint_ProxyIncludesFeedAndSearchInTotal()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/north-star\"",
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"");

        endpointBlock.Should().Contain("feedCompletedJudgments + searchCompletedJudgments",
            "weeklyCompletedJudgmentLoops proxy must sum all 5 sources including feed and search");
    }

    [Fact]
    public void NorthStarEndpoint_SourceBreakdownCoversAllFiveSources()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/north-star\"",
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"");

        foreach (var source in new[] { "feed", "discover", "shareLanding", "notification", "search" })
        {
            endpointBlock.Should().Contain(source,
                $"sourceBreakdown must include '{source}' to satisfy the 5-source requirement");
        }

        endpointBlock.Should().Contain("sourcesTracked",
            "dataQuality must enumerate the tracked sources for dashboard clarity");
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker '{startMarker}' should exist");
        end.Should().BeGreaterThan(start, $"end marker '{endMarker}' should exist after '{startMarker}'");

        return text[start..end];
    }
}
