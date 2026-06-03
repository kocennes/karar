using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Admin;

public sealed class ActivityRetentionAnalyticsTests
{
    // ── Activity: DAU / MAU ──────────────────────────────────────────────────

    [Fact]
    public void ActivityEndpoint_IncludesExplicitDauAndMauMetrics()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/activity\"",
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"");

        block.Should().Contain("dauToday",
            "activity endpoint must expose daily active users as a named metric");
        block.Should().Contain("mauThisMonth",
            "activity endpoint must expose monthly active users as a named metric");
        block.Should().Contain("dauToMauRatio",
            "activity endpoint must expose the DAU/MAU engagement ratio");
        block.Should().Contain("DATE_TRUNC('month', NOW())",
            "MAU must be computed as devices active since the start of the current calendar month");
        block.Should().Contain("CURRENT_DATE",
            "DAU must be computed against today's calendar date, not a floating interval");
    }

    [Fact]
    public void ActivityEndpoint_DauMauQueryIsSafelyParameterised()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/activity\"",
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"");

        block.Should().Contain("dauMauCmd",
            "DAU/MAU query must use a dedicated command, not be inlined in the summary query");
        block.Should().Contain("@platform",
            "DAU/MAU query must respect the optional platform filter");
        block.Should().NotContain("string.Format",
            "platform filter must be a parameter, not interpolated into SQL");
    }

    [Fact]
    public void ActivityEndpoint_DauMauArePresentInReturnedObject()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/activity\"",
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"");

        var returnIndex = block.LastIndexOf("return Results.Ok(", StringComparison.Ordinal);
        returnIndex.Should().BeGreaterThan(0,
            "activity endpoint must have a return Results.Ok statement");
        var returnBlock = block[returnIndex..];

        returnBlock.Should().Contain("dauToday",
            "dauToday must appear in the returned anonymous object");
        returnBlock.Should().Contain("mauThisMonth",
            "mauThisMonth must appear in the returned anonymous object");
        returnBlock.Should().Contain("dauToMauRatio",
            "dauToMauRatio must appear in the returned anonymous object");
    }

    // ── Retention: D1 / D7 / D30 ────────────────────────────────────────────

    [Fact]
    public void RetentionEndpoint_RequiresAdminAndClampsCohortDays()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/retention\"",
            "// ── NOTIFICATION ANALYTICS");

        block.Should().Contain("RequireAdmin(httpRequest, adminAuth)",
            "retention endpoint must be admin-only");
        block.Should().Contain("Math.Clamp(cohortDays, 7, 90)",
            "cohortDays must be clamped to [7, 90] to prevent runaway queries");
    }

    [Fact]
    public void RetentionEndpoint_ComputesD1D7D30RetentionRatesFromDevices()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/retention\"",
            "// ── NOTIFICATION ANALYTICS");

        block.Should().Contain("d1_retention",
            "SQL must compute D1 retention column");
        block.Should().Contain("d7_retention",
            "SQL must compute D7 retention column");
        block.Should().Contain("d30_retention",
            "SQL must compute D30 retention column");
        block.Should().Contain("d1RetentionPercent",
            "response must expose d1RetentionPercent");
        block.Should().Contain("d7RetentionPercent",
            "response must expose d7RetentionPercent");
        block.Should().Contain("d30RetentionPercent",
            "response must expose d30RetentionPercent");
        block.Should().Contain("FROM devices",
            "retention must be computed from the devices table");
        block.Should().Contain("INTERVAL '1 day'",
            "D1 window must be exactly 1 day");
        block.Should().Contain("INTERVAL '7 days'",
            "D7 window must be exactly 7 days");
        block.Should().Contain("INTERVAL '30 days'",
            "D30 window must be exactly 30 days");
    }

    [Fact]
    public void RetentionEndpoint_HandlesNullRetentionRatesGracefully()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/retention\"",
            "// ── NOTIFICATION ANALYTICS");

        block.Should().Contain("NULLIF(",
            "retention SQL must use NULLIF to avoid division by zero when cohort is empty");
        block.Should().Contain("IsDBNull",
            "reader must check for DBNull before reading retention values to handle empty cohorts");
    }

    [Fact]
    public void RetentionEndpoint_ReturnsTargetThresholdsForD1AndD7()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/retention\"",
            "// ── NOTIFICATION ANALYTICS");

        block.Should().Contain("targets",
            "response must include a targets object for dashboard comparison");
        block.Should().Contain("d1 = 30",
            "D1 target must be 30% as defined in engagement-strategy.md");
        block.Should().Contain("d7 = 15",
            "D7 target must be 15% as defined in engagement-strategy.md");
    }

    [Fact]
    public void RetentionEndpoint_ProvidesDailyCohortBreakdown()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/retention\"",
            "// ── NOTIFICATION ANALYTICS");

        block.Should().Contain("cohort_date",
            "SQL must group by registration date for daily cohort breakdown");
        block.Should().Contain("cohorts",
            "response must include a cohorts array");
        block.Should().Contain("d1Retained",
            "each cohort row must expose D1 retained count");
        block.Should().Contain("d7Retained",
            "each cohort row must expose D7 retained count");
        block.Should().Contain("d1Rate",
            "each cohort row must expose D1 retention rate");
        block.Should().Contain("d7Rate",
            "each cohort row must expose D7 retention rate");
    }

    [Fact]
    public void RetentionEndpoint_IsDocumented()
    {
        if (!TestRepoPaths.TryReadText(out var apiDocs, "docs", "api.md")) return;
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/retention`",
            "retention endpoint must be listed in api.md");
    }

    // ── Categories: impression / view counts ─────────────────────────────────

    [Fact]
    public void CategoriesEndpoint_RequiresAdminAuth()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/categories\"",
            "app.MapGet(\"/api/v1/admin/categories/health\"");

        block.Should().Contain("RequireAdmin(httpRequest, adminAuth)",
            "categories analytics must be admin-only");
    }

    [Fact]
    public void CategoriesEndpoint_IncludesImpressionCountsFromDiscoverEvents()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/categories\"",
            "app.MapGet(\"/api/v1/admin/categories/health\"");

        block.Should().Contain("FROM discover_events",
            "categories endpoint must join discover_events for impression counts");
        block.Should().Contain("impressions",
            "response must include an impressions field per category");
        block.Should().Contain("event_type = 'impression'",
            "impression count must filter on event_type = impression");
    }

    [Fact]
    public void CategoriesEndpoint_AcceptsDaysParameterAndClampsIt()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/categories\"",
            "app.MapGet(\"/api/v1/admin/categories/health\"");

        block.Should().Contain("@days",
            "impression window must be driven by a parametrised @days value");
        block.Should().Contain("Math.Clamp(days, 1, 90)",
            "days must be clamped to prevent runaway queries");
        block.Should().NotContain("string.Format",
            "days must be a SQL parameter, not interpolated into the query");
    }

    [Fact]
    public void CategoriesEndpoint_EmptyCategoryReturnsZeroNotNull()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/categories\"",
            "app.MapGet(\"/api/v1/admin/categories/health\"");

        block.Should().Contain("COALESCE",
            "impression count must COALESCE to 0 so categories with no discover_events appear with 0 impressions");
    }

    // ── Creation funnel dropout rates ─────────────────────────────────────────

    [Fact]
    public void CreationFunnel_ExposesPublishHoldAndDropoutRates()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"",
            "app.MapGet(\"/api/v1/admin/analytics/reports/timeseries\"");

        block.Should().Contain("publishRate",
            "creation funnel must expose the publish conversion rate");
        block.Should().Contain("holdRate",
            "creation funnel must expose the hold-for-review rate");
        block.Should().Contain("dropoutRate",
            "creation funnel must expose the dropout/deleted rate");
    }

    [Fact]
    public void CreationFunnel_RatesAreComputedNotHardcoded()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"",
            "app.MapGet(\"/api/v1/admin/analytics/reports/timeseries\"");

        block.Should().Contain("submitted - published - held",
            "dropout must be computed as posts that were neither published nor held");
        block.Should().Contain("submitted == 0 ? 0.0",
            "rates must guard against division by zero when there are no submissions");
        block.Should().NotContain("publishRate = 0.0;",
            "publishRate must not be hardcoded");
        block.Should().NotContain("dropoutRate = 0.0;",
            "dropoutRate must not be hardcoded");
    }

    [Fact]
    public void CreationFunnel_RatesAreNullForNonCreationFunnelTypes()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var block = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"",
            "app.MapGet(\"/api/v1/admin/analytics/reports/timeseries\"");

        block.Should().Contain("double? publishRate = null",
            "publishRate must be declared nullable so judgment/growth funnels return null");
        block.Should().Contain("double? holdRate = null",
            "holdRate must be declared nullable so judgment/growth funnels return null");
        block.Should().Contain("double? dropoutRate = null",
            "dropoutRate must be declared nullable so judgment/growth funnels return null");
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end   = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker '{startMarker}' should exist");
        end.Should().BeGreaterThan(start, $"end marker '{endMarker}' should exist after start");

        return text[start..end];
    }
}
