using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

// /api/v1/reports → EvaluateReportThresholdAsync → ReportThresholdService.EvaluateReporters.
// Saldırı: hesapsız, birkaç dakikalık cihazlarla (farklı ağlardan) rapor yağdırıp içeriği
// 'under_review'a almak (404, itiraz yok). Yerleşik raporcular için eşikler değişmez.
public sealed class ReportTrustWeightingTests
{
    private static readonly TimeSpan FreshDevice = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EstablishedDevice = TimeSpan.FromDays(3);

    private readonly ReportThresholdService _service = new();

    [Fact]
    public void ThreeFreshDevicesOnDistinctNetworks_ReportingSelfHarm_DoNotAutoHidePost()
    {
        var decision = _service.EvaluateReporters("post", Reporters(3, "self_harm", FreshDevice));

        decision.ShouldAutoHide.Should().BeFalse(
            "three freshly minted anonymous devices must not be able to take a post down");
        decision.NeedsAdminAttention.Should().BeTrue(
            "the critical reports still reach admins as an alert");
        decision.Priority.Should().Be("critical");
    }

    [Fact]
    public void ThreeFreshDevicesOnDistinctNetworks_DoNotAutoHideComment()
    {
        var decision = _service.EvaluateReporters("comment", Reporters(3, "spam", FreshDevice));

        decision.ShouldAutoHide.Should().BeFalse();
        decision.NeedsAdminAttention.Should().BeTrue();
    }

    [Fact]
    public void LargeFreshFloodPlusTwoEstablishedReporters_DoesNotAutoHide()
    {
        var reporters = Reporters(2, "illegal", EstablishedDevice, firstOctet: 85)
            .Concat(Reporters(30, "illegal", FreshDevice, firstOctet: 95))
            .ToList();

        var decision = _service.EvaluateReporters("post", reporters);

        decision.ShouldAutoHide.Should().BeFalse(
            "only established reporters count toward auto-hide, however many fresh devices pile on");
        decision.NeedsAdminAttention.Should().BeTrue();
    }

    [Fact]
    public void ThreeEstablishedDevicesOnDistinctNetworks_ReportingSelfHarm_AutoHideAsCritical()
    {
        var decision = _service.EvaluateReporters("post", Reporters(3, "self_harm", EstablishedDevice));

        decision.ShouldAutoHide.Should().BeTrue();
        decision.Priority.Should().Be("critical");
        decision.NeedsAdminAttention.Should().BeFalse();
    }

    [Fact]
    public void FiveEstablishedDevicesOnDistinctNetworks_AutoHidePost()
    {
        var decision = _service.EvaluateReporters("post", Reporters(5, "spam", EstablishedDevice));

        decision.ShouldAutoHide.Should().BeTrue();
        decision.Priority.Should().Be("high");
    }

    [Fact]
    public void EstablishedDevicesBehindOneNetworkBlock_StillCountAsOneCluster()
    {
        var reporters = Enumerable.Range(1, 5)
            .Select(i => new ReporterSnapshot("karar-android-dev", "10.0.1.0/24", "self_harm", 0, 0, EstablishedDevice))
            .ToList();

        var decision = _service.EvaluateReporters("post", reporters);

        decision.ShouldAutoHide.Should().BeFalse("5 × 0.3 = 1.5 stays below the critical threshold");
        decision.NeedsAdminAttention.Should().BeFalse();
    }

    [Fact]
    public void FewFreshReports_BelowAnyThreshold_KeepPendingWithoutAlert()
    {
        var decision = _service.EvaluateReporters("post", Reporters(2, "spam", FreshDevice));

        decision.Should().Be(ReportThresholdDecision.KeepPending());
    }

    [Fact]
    public void EstablishedReporterAge_IsInclusiveAt24Hours()
    {
        ReportAbuseProtectionService.IsEstablishedReporter(TimeSpan.FromHours(24)).Should().BeTrue();
        ReportAbuseProtectionService.IsEstablishedReporter(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1)).Should().BeFalse();
    }

    private static List<ReporterSnapshot> Reporters(int count, string reason, TimeSpan deviceAge, int firstOctet = 78) =>
        Enumerable.Range(1, count)
            .Select(i => new ReporterSnapshot(
                Fingerprint: "karar-android-dev",
                IpBlock: $"{firstOctet}.{i / 250}.{i % 250}.0/24",
                Reason: reason,
                AccurateCount: 0,
                TotalCount: 0,
                DeviceAge: deviceAge))
            .ToList();
}
