using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

public sealed class BrigadeProtectionTests
{
    [Fact]
    public void HotScoreSql_UsesEwmaGeoAndQuarantineGuards()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V47__hot_score_pure_sql.sql");

        migration.Should().Contain("0.3 * GREATEST(0");
        migration.Should().Contain("e.total_votes * 0.7 + e.new_ewma * 0.3");
        migration.Should().Contain("LEFT JOIN votes   v ON v.post_id = p.id AND v.is_quarantined = FALSE");
        migration.Should().Contain("COUNT(DISTINCT v.voter_region)");
        migration.Should().Contain("CASE WHEN ev.distinct_regions >= 3 THEN 1.0");
        migration.Should().Contain("0.3 + ev.distinct_regions::float / 3.0 * 0.7");
    }

    [Fact]
    public void PostDistribution_RequiresGeoSpreadAndSlowsThrottledCategories()
    {
        var job = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "PostDistributionJob.cs");

        job.Should().Contain("DistinctVoterIpBlocks < 3");
        job.Should().Contain("trendThreshold = throttled ? 6.0 : 3.0");
        job.Should().Contain("voteThreshold = throttled ? 60 : 30");
        job.Should().Contain("minAge = throttled ? TimeSpan.FromMinutes(30) : TimeSpan.FromMinutes(10)");
        job.Should().Contain("timeoutAge = throttled ? TimeSpan.FromHours(1) : TimeSpan.FromMinutes(30)");
        job.Should().Contain("COUNT(DISTINCT v.voter_ip_block)");
    }

    [Fact]
    public void PoliticalNarrativeJob_DetectsVoteRateAnomaliesAndThrottlesForTwoHours()
    {
        var job = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "PoliticalNarrativeClusterJob.cs");

        job.Should().Contain("TimeSpan.FromHours(6)");
        job.Should().Contain("TimeSpan.FromHours(2)");
        job.Should().Contain("v.created_at >= NOW() - INTERVAL '6 hours'");
        job.Should().Contain("COUNT(v.*)::int AS recent_votes");
        job.Should().Contain("COUNT(v.*)::double precision / 162.0 AS baseline_hourly");
        job.Should().Contain("r.recent_votes / 6.0 > b.baseline_hourly * @spikeMultiplier");
        job.Should().Contain("r.median_account_age_hours < @youngAccountMaxHours");
        job.Should().Contain("brigade_detected");
        job.Should().Contain("brigade_category_throttle");
        job.Should().Contain("post_id:");
        job.Should().Contain("voter_profile:");
    }

    [Fact]
    public void SuspiciousDevices_AreMarkedQuarantinedAndVotesAreAudited()
    {
        var service = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "DeviceTrustService.cs");
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V48__brigade_device_quarantine.sql");

        service.Should().Contain("is_quarantined");
        service.Should().Contain("is_quarantined = @isSuspicious");
        program.Should().Contain("brigade_quarantine");
        program.Should().Contain("trustDecision.ShouldQuarantineVote");
        migration.Should().Contain("ADD COLUMN IF NOT EXISTS is_quarantined");
    }

    [Fact]
    public void WeightedReportThreshold_MovesContentToUnderReviewAndAudits()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var reportThreshold = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "ReportThresholdService.cs");
        var reportProtection = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "ReportAbuseProtectionService.cs");

        reportThreshold.Should().Contain("Moderation:WeightedReportThresholds:Post");
        reportProtection.Should().Contain("ReportHourlyLimit = 10");
        reportProtection.Should().Contain("\"report-device\"");
        program.Should().Contain("SET status = 'under_review'");
        program.Should().Contain("brigade_under_review");
        program.Should().Contain("source = \"weighted_report_count\"");
    }
}
