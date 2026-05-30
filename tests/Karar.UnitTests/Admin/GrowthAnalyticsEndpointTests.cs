using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Admin;

public sealed class GrowthAnalyticsEndpointTests
{
    [Fact]
    public void GrowthEventsMigration_DefinesShareFunnelEventSource()
    {
        var migrationSql = TestRepoPaths.ReadText("backend", "migrations", "V42__growth_events.sql");

        migrationSql.Should().Contain("CREATE TABLE IF NOT EXISTS growth_events");
        migrationSql.Should().Contain("'share_landing_opened'");
        migrationSql.Should().Contain("'share_landing_vote_attempt'");
        migrationSql.Should().Contain("'share_landing_completed_judgment'");
        migrationSql.Should().Contain("'share_to_install'");
        migrationSql.Should().Contain("idx_growth_events_type_created");
        migrationSql.Should().Contain("idx_growth_events_post_created");
        migrationSql.Should().Contain("idx_growth_events_device_created");
    }

    [Fact]
    public void GrowthEventsIngestion_AcceptsShareFunnelTypesAndInsertsToDb()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/growth-events\"",
            "app.MapGet(\"/api/v1/admin/analytics/growth\"");

        endpointBlock.Should().Contain("share_landing_opened");
        endpointBlock.Should().Contain("share_landing_vote_attempt");
        endpointBlock.Should().Contain("share_landing_completed_judgment");
        endpointBlock.Should().Contain("share_to_install");
        endpointBlock.Should().Contain("INVALID_GROWTH_EVENT");
        endpointBlock.Should().Contain("INSERT INTO growth_events");
        endpointBlock.Should().Contain("RequestDevice requestDevice");
        endpointBlock.Should().Contain("GetOptionalUserId(httpRequest, jwtService)");
        endpointBlock.Should().Contain("RequireRateLimiting(\"growth-events\")");
    }

    [Fact]
    public void GrowthEventsIngestion_AcceptsNotificationCompletedJudgmentEventType()
    {
        var migrationSql = TestRepoPaths.ReadText("backend", "migrations", "V43__notification_completed_judgment.sql");
        migrationSql.Should().Contain("notification_completed_judgment");
        migrationSql.Should().Contain("ALTER TABLE growth_events");

        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/growth-events\"",
            "app.MapGet(\"/api/v1/admin/analytics/growth\"");

        endpointBlock.Should().Contain("notification_completed_judgment",
            "growth-events endpoint must accept notification attribution events");
    }

    [Fact]
    public void GrowthAnalyticsEndpoint_CalculatesNotificationToCompletedJudgment()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/growth\"",
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"");

        endpointBlock.Should().Contain("notification_completed_judgment",
            "analytics endpoint must query notification_completed_judgment from growth_events");
        endpointBlock.Should().Contain("notificationCompletedJudgments",
            "analytics endpoint must expose the raw count");
        endpointBlock.Should().Contain("RatePercent(notificationCompletedJudgments, notificationOpened)",
            "notificationToCompletedJudgment must be a real rate, not a 0.0 placeholder");
        endpointBlock.Should().NotContain("notificationToCompletedJudgment = 0.0",
            "placeholder must be replaced with actual calculation");
    }

    [Fact]
    public void GrowthAnalyticsEndpoint_AggregatesCoreGrowthLoops()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/growth\"",
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
        endpointBlock.Should().Contain("Math.Clamp(days, 1, 90)");
        endpointBlock.Should().Contain("FROM growth_events");
        endpointBlock.Should().Contain("FROM discover_events");
        endpointBlock.Should().Contain("FROM notification_events");
        endpointBlock.Should().Contain("share_landing_opened");
        endpointBlock.Should().Contain("share_landing_vote_attempt");
        endpointBlock.Should().Contain("share_landing_completed_judgment");
        endpointBlock.Should().Contain("share_to_install");
        endpointBlock.Should().Contain("shareToCompletedJudgmentRate");
        endpointBlock.Should().Contain("kFactorEstimate");
        endpointBlock.Should().Contain("discoverToCompletedJudgment");
        endpointBlock.Should().Contain("notificationToCompletedJudgment");
        endpointBlock.Should().Contain("firebaseExportRequiredForCrossDeviceAttribution");
        endpointBlock.Should().Contain("notificationCompletedJudgmentNeedsClientSourceAttribution",
            "admin UI must know that notification-to-judgment is still a proxy until client source attribution is complete");

        var apiDocs = TestRepoPaths.ReadText("docs", "api.md");
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/growth`");
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
