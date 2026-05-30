using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Admin;

public sealed class FeedQualityEndpointTests
{
    [Fact]
    public void FeedQualityEndpoint_RequiresAdminAuth()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)",
            "feed-quality endpoint must verify admin credentials before returning data");
    }

    [Fact]
    public void FeedQualityEndpoint_QueriesDiscoverEvents()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("FROM discover_events",
            "feed-quality must aggregate data from discover_events table");
        endpointBlock.Should().Contain("Math.Clamp(days, 1, 90)",
            "days parameter must be clamped to [1, 90]");
    }

    [Fact]
    public void FeedQualityEndpoint_AggregatesAllRequiredEventTypes()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("'impression'");
        endpointBlock.Should().Contain("'dwell'");
        endpointBlock.Should().Contain("'skip'");
        endpointBlock.Should().Contain("'vote'");
        endpointBlock.Should().Contain("'comment_open'");
        endpointBlock.Should().Contain("'share'");
        endpointBlock.Should().Contain("'not_interested'");
        endpointBlock.Should().Contain("avg_dwell_seconds",
            "endpoint must expose average dwell time for quality analysis");
    }

    [Fact]
    public void FeedQualityEndpoint_RatesAreNotPlaceholders()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("dwellRate",
            "dwellRate must be returned");
        endpointBlock.Should().Contain("voteRate",
            "voteRate must be returned");
        endpointBlock.Should().Contain("skipRate",
            "skipRate must be returned");
        endpointBlock.Should().Contain("notInterestedRate",
            "notInterestedRate must be returned");
        endpointBlock.Should().NotContain("dwellRate = 0.0",
            "rates must be computed from data, not hardcoded");
        endpointBlock.Should().NotContain("voteRate = 0.0",
            "rates must be computed from data, not hardcoded");
    }

    [Fact]
    public void FeedQualityEndpoint_IncludesRankingReasonBreakdown()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("ranking_reason",
            "endpoint must break down metrics by ranking_reason from JSONB metadata");
        endpointBlock.Should().Contain("byRankingReason",
            "response must include byRankingReason array");
        endpointBlock.Should().Contain("FqRate(rrSkip, rrImpr)",
            "ranking_reason breakdown skip rate must be computed via FqRate helper");
        endpointBlock.Should().Contain("FqRate(rrNotInt, rrImpr)",
            "ranking_reason breakdown not_interested rate must be computed via FqRate helper");
    }

    [Fact]
    public void FeedQualityEndpoint_RankingBucketIncludesHealthStatus()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("healthStatus      = rrHealth",
            "each ranking bucket must include a computed healthStatus field");
        endpointBlock.Should().Contain("rrHealth",
            "bucket health must be stored in a variable, not inlined as a hardcoded string");
        endpointBlock.Should().Contain("rrImpr >= MinImpressions && (rrSkipRate > 55 || rrNotIntRate > 10)",
            "bucket critical alarm must be impression-guarded and check skip and not_interested thresholds");
        endpointBlock.Should().Contain("rrImpr >= MinImpressions && rrVoteRate < 8",
            "bucket warning must be impression-guarded and check vote rate threshold");
        endpointBlock.Should().NotContain("healthStatus = \"healthy\"",
            "bucket health must not be hardcoded to healthy");
    }

    [Fact]
    public void FeedQualityEndpoint_HealthIsCalculatedFromRates()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("healthStatus",
            "health status must be a derived variable, not a hardcoded string");
        endpointBlock.Should().Contain("healthReasons",
            "health reasons must be built from rate checks");
        endpointBlock.Should().Contain("impressions >= MinImpressions && skipRate > 55",
            "skip rate critical alarm must be volume-guarded and use threshold 55");
        endpointBlock.Should().Contain("impressions >= MinImpressions && notInterestedRate > 10",
            "not_interested critical alarm must be volume-guarded and use threshold 10");
        endpointBlock.Should().Contain("voteRate < 8",
            "low vote rate must trigger a warning");
        endpointBlock.Should().Contain("dwellRate < 25",
            "low dwell rate must trigger a warning");
        endpointBlock.Should().Contain("MinImpressions",
            "rate alarms must be guarded by a minimum impression count to avoid noise");
        endpointBlock.Should().Contain("\"critical\"",
            "endpoint must be able to return critical status");
        endpointBlock.Should().Contain("\"warning\"",
            "endpoint must be able to return warning status");
        endpointBlock.Should().Contain("\"healthy\"",
            "endpoint must return healthy when all thresholds are within range");
        endpointBlock.Should().NotContain("status = \"healthy\"",
            "health status must not be hardcoded to healthy");
    }

    [Fact]
    public void FeedQualityDrillDownEndpoint_RequiresAdminAuthAndQueriesDiscoverEvents()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/posts\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)",
            "drill-down endpoint must verify admin credentials");
        endpointBlock.Should().Contain("Math.Clamp(days, 1, 90)",
            "days must be clamped to [1, 90]");
        endpointBlock.Should().Contain("FROM discover_events",
            "drill-down must aggregate from discover_events");
        endpointBlock.Should().Contain("GROUP BY post_id",
            "results must be grouped per post");
    }

    [Fact]
    public void FeedQualityDrillDownEndpoint_FiltersAndSortsCorrectly()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/posts\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("@rankingReason",
            "endpoint must accept optional rankingReason filter");
        endpointBlock.Should().Contain("metadata->>'ranking_reason' = @rankingReason",
            "rankingReason filter must query JSONB metadata field");
        endpointBlock.Should().Contain("ORDER BY",
            "results must be ordered by worst-performing posts first");
        endpointBlock.Should().Contain("impressions >= 5",
            "drill-down must ignore tiny sample sizes to reduce noisy false positives");
        endpointBlock.Should().Contain("not_interested * 1.0 / NULLIF(impressions, 0) >= 0.08",
            "not_interested rate must participate in problem-post filtering");
        endpointBlock.Should().Contain("votes * 1.0 / NULLIF(impressions, 0) < 0.08",
            "low vote rate must participate in problem-post filtering");
        endpointBlock.Should().Contain("CASE WHEN votes * 1.0 / NULLIF(impressions, 0) < 0.08",
            "low vote rate must participate in worst-post ordering");
        endpointBlock.Should().Contain("LIMIT 50",
            "result set must be capped to prevent large payloads");
    }

    [Fact]
    public void FeedQualityDrillDownEndpoint_RatesAreCalculatedNotHardcoded()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/posts\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("skipRate",
            "skipRate must be returned per post");
        endpointBlock.Should().Contain("notInterestedRate",
            "notInterestedRate must be returned per post");
        endpointBlock.Should().Contain("voteRate",
            "voteRate must be returned per post");
        endpointBlock.Should().Contain("Dr(sk, impr)",
            "rates must use the local Dr() helper, not hardcoded values");
        endpointBlock.Should().NotContain("skipRate = 0.0",
            "skipRate must not be hardcoded");
    }

    [Fact]
    public void FeedQualityDrillDownEndpoint_JoinsPostsAndCategoriesForMetadata()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/posts\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("JOIN posts",
            "drill-down must join posts table to retrieve post metadata");
        endpointBlock.Should().Contain("JOIN categories",
            "drill-down must join categories table for category name");
        endpointBlock.Should().Contain("p.title",
            "post title must be selected");
        endpointBlock.Should().Contain("c.name",
            "category name must be selected");
        endpointBlock.Should().Contain("p.status",
            "post status must be returned so admin can see removed/hidden posts");
        endpointBlock.Should().Contain("p.trend_score",
            "trend score must be returned for context");
        endpointBlock.Should().Contain("p.created_at",
            "post creation date must be returned");
    }

    [Fact]
    public void FeedQualityDrillDownEndpoint_AggregatesReportCount()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/posts\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("LEFT JOIN reports",
            "reports must be left-joined so posts with zero reports are still returned");
        endpointBlock.Should().Contain("target_type = 'post'",
            "report join must filter by target_type = post to avoid counting comment reports");
        endpointBlock.Should().Contain("COUNT(r.id)",
            "report count must be aggregated, not just joined");
        endpointBlock.Should().Contain("reportCount",
            "report count must be exposed in the response");
    }

    [Fact]
    public void FeedQualityDrillDownEndpoint_IsDocumented()
    {
        var apiDocs = TestRepoPaths.ReadText("docs", "api.md");
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/feed-quality/posts`",
            "drill-down endpoint must be listed in api.md");
    }

    [Fact]
    public void FeedQualityEndpoint_IsDocumented()
    {
        var apiDocs = TestRepoPaths.ReadText("docs", "api.md");
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/feed-quality`",
            "feed-quality endpoint must be listed in api.md");
    }

    [Fact]
    public void TimeseriesEndpoint_RequiresAdminAuth()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/timeseries\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)",
            "timeseries endpoint must verify admin credentials");
    }

    [Fact]
    public void TimeseriesEndpoint_UsesGenerateSeriesAndDiscoverEvents()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/timeseries\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("generate_series(",
            "timeseries must fill empty days using generate_series");
        endpointBlock.Should().Contain("(@days - 1) * INTERVAL '1 day'",
            "timeseries should return exactly the requested number of calendar days including today");
        endpointBlock.Should().Contain("FROM discover_events",
            "timeseries must query discover_events table");
        endpointBlock.Should().Contain("Math.Clamp(days, 1, 90)",
            "days parameter must be clamped to [1, 90]");
    }

    [Fact]
    public void TimeseriesEndpoint_ComputesAllRates()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/timeseries\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("dwellRate",
            "dwellRate must be returned per day");
        endpointBlock.Should().Contain("voteRate",
            "voteRate must be returned per day");
        endpointBlock.Should().Contain("skipRate",
            "skipRate must be returned per day");
        endpointBlock.Should().Contain("notInterestedRate",
            "notInterestedRate must be returned per day");
        endpointBlock.Should().Contain("Rt(",
            "rates must be computed via helper function, not hardcoded");
    }

    [Fact]
    public void TimeseriesEndpoint_ComputesHealthStatusPerDay()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/timeseries\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("healthStatus",
            "health status must be computed per day");
        endpointBlock.Should().Contain("MinImpressions",
            "health alarms must be guarded by minimum impression count");
        endpointBlock.Should().Contain("\"critical\"",
            "timeseries must be able to return critical health status");
        endpointBlock.Should().Contain("\"warning\"",
            "timeseries must be able to return warning health status");
        endpointBlock.Should().Contain("\"healthy\"",
            "timeseries must return healthy when thresholds are not breached");
    }

    [Fact]
    public void TimeseriesEndpoint_IsDocumented()
    {
        var apiDocs = TestRepoPaths.ReadText("docs", "api.md");
        apiDocs.Should().Contain("- `GET /api/v1/admin/analytics/feed-quality/timeseries`",
            "timeseries endpoint must be listed in api.md");
    }

    [Fact]
    public void ExportEndpoint_RequiresAdminAuthAndValidatesFormat()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/export\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("RequireAdmin(httpRequest, adminAuth)",
            "export endpoint must verify admin credentials");
        endpointBlock.Should().Contain("TryGetAdminEmail(httpRequest)",
            "export audit log must be tied to the authenticated admin identity");
        endpointBlock.Should().Contain("Math.Clamp(days, 1, 90)",
            "days parameter must be clamped to [1, 90]");
        endpointBlock.Should().Contain("INVALID_FORMAT",
            "export endpoint must reject unsupported formats");
        endpointBlock.Should().Contain("string.Equals(format, \"csv\", StringComparison.OrdinalIgnoreCase)",
            "csv must be the only supported MVP export format");
    }

    [Fact]
    public void ExportEndpoint_ReturnsCsvWithDownloadHeaders()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/export\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("text/csv",
            "export endpoint must return CSV content type");
        endpointBlock.Should().Contain("Content-Disposition",
            "export endpoint must set a download filename");
        endpointBlock.Should().Contain("feed-quality-{days}d-",
            "download filename must identify the feed-quality export window");
        endpointBlock.Should().Contain("date,impressions,dwell_rate,vote_rate,skip_rate,not_interested_rate,health_status",
            "CSV header must match the public export contract");
    }

    [Fact]
    public void ExportEndpoint_UsesTimeseriesDataAndComputesRates()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/export\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("generate_series(",
            "CSV export must include empty days using generate_series");
        endpointBlock.Should().Contain("FROM discover_events",
            "CSV export must read feed quality events from discover_events");
        endpointBlock.Should().Contain("CsvRate(",
            "CSV export rates must be computed, not hardcoded");
        endpointBlock.Should().Contain("CsvHealth(",
            "CSV export health status must be computed from rates");
    }

    [Fact]
    public void ExportEndpoint_UsesRateLimitPolicy()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/export\"",
            "// ── GROWTH EVENTS INGESTION");

        programText.Should().Contain("options.AddPolicy(\"admin-analytics-export\"",
            "CSV export can be expensive and must have a dedicated admin rate limit");
        programText.Should().Contain("PermitLimit = 10",
            "MVP export rate limit should allow short manual admin bursts without enabling scraping");
        programText.Should().Contain("Window = TimeSpan.FromMinutes(1)",
            "export rate limit window must be minute-based");
        endpointBlock.Should().Contain("RequireRateLimiting(\"admin-analytics-export\")",
            "export endpoint must be attached to the dedicated rate limit policy");
    }

    [Fact]
    public void ExportEndpoint_WritesAdminAuditLog()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/admin/analytics/feed-quality/export\"",
            "// ── GROWTH EVENTS INGESTION");

        endpointBlock.Should().Contain("BeginTransactionAsync()",
            "audit logging should be committed explicitly after export generation");
        endpointBlock.Should().Contain("LogAdminActionAsync(",
            "feed quality exports must leave an admin audit trail");
        endpointBlock.Should().Contain("\"analytics_exported\"",
            "audit action must describe the export behavior");
        endpointBlock.Should().Contain("\"analytics\"",
            "audit target type must group analytics/reporting actions");
        endpointBlock.Should().Contain("report=feed_quality",
            "audit note must identify which report was exported");
        endpointBlock.Should().Contain("days={days}",
            "audit note must preserve the export window");
        endpointBlock.Should().Contain("format=csv",
            "audit note must preserve the export format");
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
