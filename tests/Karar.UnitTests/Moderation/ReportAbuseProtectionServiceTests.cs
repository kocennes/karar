using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

public sealed class ReportAbuseProtectionServiceTests
{
    // ── ComputeReportWeight — bağımsız reporter ─────────────────────────────

    [Fact]
    public void ComputeReportWeight_ReturnsFullWeight_WhenNoExistingReporters()
    {
        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-a",
            reporterIpBlock: "10.0.1.0/24",
            existingReporters: []);

        weight.Should().Be(1.0);
    }

    [Fact]
    public void ComputeReportWeight_ReturnsFullWeight_WhenFingerprintAndSubnetAreBothUnique()
    {
        var existing = new[]
        {
            ("device-b", (string?)"10.0.2.0/24"),
            ("device-c", (string?)"10.0.3.0/24"),
        };

        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-a",
            reporterIpBlock: "10.0.1.0/24",
            existingReporters: existing);

        weight.Should().Be(1.0);
    }

    // ── ComputeReportWeight — aynı subnet (/24) ─────────────────────────────

    [Fact]
    public void ComputeReportWeight_ReturnsSubnetPenalty_WhenSameIpBlock()
    {
        var existing = new[]
        {
            ("device-b", (string?)"10.0.1.0/24"),
        };

        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-a",
            reporterIpBlock: "10.0.1.0/24",
            existingReporters: existing);

        weight.Should().Be(0.3);
    }

    [Fact]
    public void ComputeReportWeight_ReturnsSubnetPenalty_WhenMultipleExistingReportersFromSameSubnet()
    {
        var existing = new[]
        {
            ("device-b", (string?)"10.0.1.0/24"),
            ("device-c", (string?)"10.0.1.0/24"),
            ("device-d", (string?)"10.0.1.0/24"),
        };

        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-a",
            reporterIpBlock: "10.0.1.0/24",
            existingReporters: existing);

        weight.Should().Be(0.3);
    }

    [Fact]
    public void ComputeReportWeight_ReturnsFullWeight_WhenReporterHasNoIpBlock()
    {
        // Reporter without IP data (e.g., behind Tor/VPN that strips IP)
        // cannot be matched against existing subnet reporters → full weight
        var existing = new[]
        {
            ("device-b", (string?)"10.0.1.0/24"),
        };

        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-a",
            reporterIpBlock: null,
            existingReporters: existing);

        weight.Should().Be(1.0);
    }

    // ── ComputeReportWeight — aynı fingerprint (Sybil guard) ────────────────

    [Fact]
    public void ComputeReportWeight_ReturnsSybilPenalty_WhenSameFingerprint()
    {
        // DB UNIQUE constraint normally prevents this, but belt-and-suspenders.
        var existing = new[]
        {
            ("device-a", (string?)"10.0.2.0/24"),
        };

        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-a",
            reporterIpBlock: "10.0.1.0/24",
            existingReporters: existing);

        weight.Should().Be(0.1);
    }

    [Fact]
    public void ComputeReportWeight_FingerprintCheckTakesPriorityOverSubnet()
    {
        // Same fingerprint AND same subnet → fingerprint penalty wins (0.1 < 0.3)
        var existing = new[]
        {
            ("device-a", (string?)"10.0.1.0/24"),
        };

        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-a",
            reporterIpBlock: "10.0.1.0/24",
            existingReporters: existing);

        weight.Should().Be(0.1);
    }

    // ── Koordineli saldırı senaryosu ─────────────────────────────────────────

    [Fact]
    public void ComputeReportWeight_DetectsBrigadeFromSingleSubnet()
    {
        // Bir koordineli saldırı grubu: 5 farklı cihaz, aynı /24 subnet
        var subnetReporters = Enumerable.Range(1, 5)
            .Select(i => ($"device-{i}", (string?)"192.168.1.0/24"))
            .ToList();

        // 6. cihaz aynı subnetten → 0.3 ağırlık alır
        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "device-6",
            reporterIpBlock: "192.168.1.0/24",
            existingReporters: subnetReporters);

        weight.Should().Be(0.3);
    }

    [Fact]
    public void ComputeReportWeight_GivesFullWeightToIndependentReporterAmongBrigade()
    {
        // Brigade reporters all on same subnet
        var brigadeReporters = Enumerable.Range(1, 5)
            .Select(i => ($"device-{i}", (string?)"192.168.1.0/24"))
            .ToList();

        // Independent reporter from different subnet → full weight
        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: "real-user",
            reporterIpBlock: "85.0.10.0/24",
            existingReporters: brigadeReporters);

        weight.Should().Be(1.0);
    }

    // ── Ağırlık sınırları ───────────────────────────────────────────────────

    [Theory]
    [InlineData(1.0)]
    [InlineData(0.3)]
    [InlineData(0.1)]
    public void ComputeReportWeight_AlwaysReturnsOneOfThreeDefinedValues(double expectedWeight)
    {
        expectedWeight.Should().BeOneOf(0.1, 0.3, 1.0);
    }

    // ── EvaluateReportThreshold — ağırlıklı sayım mantığı ────────────────────

    [Fact]
    public void WeightedSum_FiveIndependentReporters_ReachesThreshold()
    {
        // Docs: post threshold = 5.0 weighted reporters
        var reporters = Enumerable.Range(1, 5)
            .Select(i => ($"device-{i}", (string?)$"10.0.{i}.0/24"))
            .ToList();

        // Compute weight for each reporter against all others
        var totalWeight = reporters.Select((r, i) =>
        {
            var others = reporters.Where((_, j) => j != i);
            return ReportAbuseProtectionService.ComputeReportWeight(r.Item1, r.Item2, others);
        }).Sum();

        totalWeight.Should().Be(5.0); // 5 fully independent reporters = 5.0
    }

    [Fact]
    public void WeightedSum_FiveBrigadeReportersSameSubnet_DoesNotReachThreshold()
    {
        // 5 reporters from same /24 subnet. Each is compared against ALL others in the
        // batch, so every one sees same-subnet peers → each gets 0.3.
        var reporters = Enumerable.Range(1, 5)
            .Select(i => ($"device-{i}", (string?)"192.168.1.0/24"))
            .ToList();

        var totalWeight = reporters.Select((r, i) =>
        {
            var others = reporters.Where((_, j) => j != i);
            return ReportAbuseProtectionService.ComputeReportWeight(r.Item1, r.Item2, others);
        }).Sum();

        // 5 × 0.3 = 1.5, well below post threshold of 5.0
        totalWeight.Should().BeApproximately(1.5, precision: 0.01);
    }

    [Fact]
    public void WeightedSum_MixedBrigadeAndIndependent_PartialThreshold()
    {
        // 3 brigade reporters from same subnet + 2 independent reporters
        var reporters = new List<(string Fingerprint, string? IpBlock)>
        {
            ("brigade-1", "192.168.1.0/24"),
            ("brigade-2", "192.168.1.0/24"),
            ("brigade-3", "192.168.1.0/24"),
            ("legit-1",   "85.0.10.0/24"),
            ("legit-2",   "85.0.11.0/24"),
        };

        var totalWeight = reporters.Select((r, i) =>
        {
            var others = reporters.Where((_, j) => j != i);
            return ReportAbuseProtectionService.ComputeReportWeight(r.Fingerprint, r.IpBlock, others);
        }).Sum();

        // Each brigade reporter sees same-subnet peers → 0.3 each (3 × 0.3 = 0.9)
        // legit-1 and legit-2 are on unique subnets → 1.0 each (2 × 1.0 = 2.0)
        // Total = 0.9 + 2.0 = 2.9 — below post threshold of 5.0
        totalWeight.Should().BeApproximately(2.9, precision: 0.01);
    }
}
