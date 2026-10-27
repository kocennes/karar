using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

// Rapor tabanlı sansür: cihaz kaydı hesapsız ve ucuzdur (tek IP'den saatte 5 cihaz).
// Ham "bekleyen rapor sayısı < 3" feed filtresi, aynı IP'den 3 taze cihazla herhangi bir postu
// tüm feed'lerden düşürüyordu; 3 farklı ağdan 3 taze cihazın 'self_harm' raporu da postu
// 'under_review' durumuna (404, itiraz edilemez) alıyordu. SQL Postgres olmadan çalıştırılamadığı
// için feed/trend tarafı kaynak metin üzerinden doğrulanır; ağırlık mantığı davranışsal testlerde.
public sealed class ReportCensorshipContractTests
{
    private const string TrustedReporterCount =
        "COUNT(DISTINCT COALESCE(r.reporter_ip_block, r.reporter_device_id::text))";

    private const string EstablishedDeviceFilter =
        "rd.created_at <= NOW() - INTERVAL '24 hours'";

    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void FeedQueries_NoLongerHidePostsOnRawPendingReportCount()
    {
        ProgramText.Should().NotContain(
            "(SELECT COUNT(*) FROM reports r WHERE r.target_type = 'post' AND r.target_id = p.id AND r.status = 'pending') < 3",
            "three reports from freshly minted devices behind one IP must not drop a post from every feed");
        Regex.IsMatch(
                ProgramText,
                @"SELECT COUNT\(\*\) FROM reports r\s+WHERE r\.target_type = 'post' AND r\.target_id = p\.id AND r\.status = 'pending'\s+\) < 3")
            .Should().BeFalse("the multi-line feed variants must not use the raw count either");
    }

    [Fact]
    public void FeedQueries_HoldPostsOnlyForIndependentEstablishedReporters()
    {
        CountOccurrences(ProgramText, TrustedReporterCount).Should().Be(14,
            "all 14 feed/discover distribution filters must count distinct reporter networks");
        CountOccurrences(ProgramText, "JOIN devices rd ON rd.id = r.reporter_device_id").Should().Be(14);
        CountOccurrences(ProgramText, EstablishedDeviceFilter).Should().Be(14,
            "reports from devices younger than the established age must not hold a post");
        ReportAbuseProtectionService.EstablishedReporterMinAge.Should().Be(TimeSpan.FromHours(24),
            "the SQL feed hold and the C# auto-hide must agree on what an established reporter is");
    }

    [Fact]
    public void ReportThreshold_EvaluatesReportersWithDeviceAge()
    {
        var block = SliceBlock(
            ProgramText,
            "static async Task<ReportThresholdDecision> EvaluateReportThresholdAsync(",
            "static string? GetClientIpBlock(HttpRequest request)");

        block.Should().Contain("d.created_at", "device age is needed to discount freshly minted reporters");
        block.Should().Contain("reportThresholdService.EvaluateReporters(",
            "weighting must go through the unit-tested service method");
    }

    [Fact]
    public void ReportEndpoint_AlertsAdminsInsteadOfHidingOnUntrustedReports()
    {
        var endpoint = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/reports\"",
            "app.MapPost(\"/api/v1/feedback\"");

        endpoint.Should().Contain("threshold.NeedsAdminAttention");
        endpoint.Should().Contain("AlertAdminsAboutUntrustedReportsAsync(");

        var helper = SliceBlock(
            ProgramText,
            "static async Task AlertAdminsAboutUntrustedReportsAsync(",
            "static async Task AutoHideReportedTargetAsync(");
        helper.Should().Contain("'report_threshold_untrusted'");
        helper.Should().Contain("WHERE NOT EXISTS", "one unresolved alert per target is enough");
    }

    [Fact]
    public void ModerationAppeal_AcceptsReportDrivenUnderReviewContent()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/users/me/moderation-appeals\"",
            "app.MapPost(\"/api/v1/users/me/blocked\"");

        block.Should().Contain("AND status IN ('auto_hidden', 'deleted', 'under_review')",
            "report-driven auto-hide sets 'under_review'; its author must be able to appeal");
    }

    [Fact]
    public void LatestTrendScoreFunction_PenalisesOnlyIndependentEstablishedReports()
    {
        var body = LatestFunctionBody("CREATE OR REPLACE FUNCTION refresh_trend_scores");

        body.Should().Contain(TrustedReporterCount,
            "a few fresh-device reports must not cut trend by 90% and stall distribution");
        body.Should().Contain(EstablishedDeviceFilter);
        body.Should().NotContain("d.fingerprint");
        body.Should().Contain("v.is_quarantined = FALSE");
        body.Should().Contain("v.is_suppressed = FALSE");
    }

    private static string LatestFunctionBody(string marker)
    {
        var dir = TestRepoPaths.FilePath("backend", "migrations");
        var latest = Directory.GetFiles(dir, "V*.sql")
            .Select(path => new
            {
                Path = path,
                Version = int.Parse(Regex.Match(Path.GetFileName(path), @"^V(\d+)__").Groups[1].Value),
                Text = File.ReadAllText(path)
            })
            .Where(m => m.Text.Contains(marker, StringComparison.Ordinal))
            .OrderBy(m => m.Version)
            .ThenBy(m => Path.GetFileName(m.Path), StringComparer.Ordinal)
            .Last()
            .Text;

        var start = latest.LastIndexOf(marker, StringComparison.Ordinal);
        var bodyStart = latest.IndexOf("$$", start, StringComparison.Ordinal);
        var bodyEnd = latest.IndexOf("$$", bodyStart + 2, StringComparison.Ordinal);
        return latest[bodyStart..bodyEnd];
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
