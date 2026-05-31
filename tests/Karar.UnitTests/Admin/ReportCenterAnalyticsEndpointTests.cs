using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Admin;

public sealed class ReportCenterAnalyticsEndpointTests
{
    [Fact]
    public void ActivityEndpoint_RequiresAdminAndSupportsPreviousPeriod()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/activity\"",
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
        endpointBlock.Should().Contain("NormalizeAnalyticsRange(from, to, groupBy)");
        endpointBlock.Should().Contain("PreviousFrom");
        endpointBlock.Should().Contain("previous");
        endpointBlock.Should().Contain("@platform");
        endpointBlock.Should().Contain("@userType");
        endpointBlock.Should().Contain("@source");
        endpointBlock.Should().Contain("@categoryId");
    }

    [Fact]
    public void FunnelAndReportTimeseriesEndpoints_AreAdminOnlyAndAggregate()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var funnelBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/funnels\"",
            "app.MapGet(\"/api/v1/admin/analytics/reports/timeseries\"");
        funnelBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
        funnelBlock.Should().Contain("type is not (\"judgment\" or \"creation\" or \"growth\")");
        funnelBlock.Should().Contain("FROM discover_events");
        funnelBlock.Should().Contain("FROM growth_events");

        var reportsBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/reports/timeseries\"",
            "app.MapGet(\"/api/v1/admin/analytics/export\"");
        reportsBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
        reportsBlock.Should().Contain("generate_series(");
        reportsBlock.Should().Contain("FROM reports");
        reportsBlock.Should().Contain("@reason");
        reportsBlock.Should().Contain("@status");
    }

    [Fact]
    public void ExportEndpoint_RequiresAdminAuditsAndSupportsCsvJson()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/export\"",
            "app.MapPost(\"/api/v1/admin/analytics/scheduled-reports\"");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
        endpointBlock.Should().Contain("TryGetAdminEmail(httpRequest)");
        endpointBlock.Should().Contain("report is not (\"activity\" or \"moderation\" or \"growth\")");
        endpointBlock.Should().Contain("format is not (\"csv\" or \"json\")");
        endpointBlock.Should().Contain("BuildDictionaryCsv(rows)");
        endpointBlock.Should().Contain("JsonSerializer.Serialize");
        endpointBlock.Should().Contain("LogAdminActionAsync(");
        endpointBlock.Should().Contain("\"analytics_exported\"");
        endpointBlock.Should().Contain("RequireRateLimiting(\"admin-analytics-export\")");
    }

    [Fact]
    public void ScheduledReportsEndpoint_RequiresAdminPersistsAndAudits()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/admin/analytics/scheduled-reports\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)");
        endpointBlock.Should().Contain("TryGetAdminEmail(httpRequest)");
        endpointBlock.Should().Contain("INSERT INTO admin_scheduled_reports");
        endpointBlock.Should().Contain("LogAdminActionAsync(");
        endpointBlock.Should().Contain("\"analytics_scheduled_report_created\"");
        endpointBlock.Should().Contain("RequireRateLimiting(\"admin-analytics-export\")");
    }

    [Fact]
    public void ReportCenterEndpoints_AreDocumented()
    {
        if (!TestRepoPaths.TryReadText(out var apiDocs, "docs", "api.md")) return;

        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/activity`");
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/funnels`");
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/reports/timeseries`");
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/export`");
        apiDocs.Should().Contain("- `POST /api/v1/admin/analytics/scheduled-reports`");
    }

    [Fact]
    public void ScheduledReportsMigration_DoesNotStoreEndUserPii()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V50__admin_scheduled_reports.sql");

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS admin_scheduled_reports");
        migration.Should().Contain("filters     JSONB");
        migration.Should().NotContain("user_id");
        migration.Should().NotContain("device_id");
        migration.Should().NotContain("email");
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
