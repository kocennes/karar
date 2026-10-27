using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Security;

// Resmi istemci (lib/core/auth/device_service.dart) her kurulumda sabit
// 'karar-<platform>-dev' fingerprint'i gönderir; değer tamamen istemci kontrolündedir.
// Bu yüzden fingerprint eşitliği/prefix'i kötüye kullanım sinyali olamaz:
//   * rapor ağırlığında aynı fingerprint 0.1 cezası meşru Android raporcularını eziyordu,
//   * oy brigade guard'ı LEFT(fingerprint, 8) kümesiyle her popüler postta organik oyları bastırıyordu,
//   * trend skoru COUNT(DISTINCT fingerprint) ile organik oyları platform başına 1'e indiriyordu,
// rastgele fingerprint üreten bot ise hepsinden kaçıyordu.
public sealed class ClientFingerprintSignalTests
{
    private const string OfficialAndroidFingerprint = "karar-android-dev";

    [Fact]
    public void ReportWeight_OfficialClientsOnDistinctNetworks_EachCountFully()
    {
        var reporters = Enumerable.Range(1, 5)
            .Select(i => (Fingerprint: OfficialAndroidFingerprint, IpBlock: (string?)$"85.10.{i}.0/24"))
            .ToList();

        var weights = reporters.Select((r, i) =>
            ReportAbuseProtectionService.ComputeReportWeight(
                r.Fingerprint,
                r.IpBlock,
                reporters.Where((_, j) => j != i))).ToList();

        weights.Should().AllSatisfy(w => w.Should().Be(1.0),
            "independent app users must not be penalised for sharing the app's constant fingerprint");
        new ReportThresholdService().Evaluate("post", weights.Sum(), 0).ShouldAutoHide.Should().BeTrue(
            "five independent reporters must reach the post threshold of 5.0");
    }

    [Fact]
    public void ReportWeight_OfficialClientsReportingSelfHarm_ReachCriticalThreshold()
    {
        var reporters = Enumerable.Range(1, 3)
            .Select(i => (Fingerprint: OfficialAndroidFingerprint, IpBlock: (string?)$"85.20.{i}.0/24"))
            .ToList();

        var total = reporters.Select((r, i) =>
            ReportAbuseProtectionService.ComputeReportWeight(
                r.Fingerprint,
                r.IpBlock,
                reporters.Where((_, j) => j != i))).Sum();

        new ReportThresholdService().Evaluate("post", total, total).Priority.Should().Be("critical");
    }

    [Fact]
    public void ReportWeight_SameSubnetPenalty_StillAppliesRegardlessOfFingerprint()
    {
        var weight = ReportAbuseProtectionService.ComputeReportWeight(
            reporterFingerprint: OfficialAndroidFingerprint,
            reporterIpBlock: "10.0.1.0/24",
            existingReporters: [(OfficialAndroidFingerprint, "10.0.1.0/24")]);

        weight.Should().Be(0.3, "network-block clustering is still a coordination signal");
    }

    [Fact]
    public void VoteBrigadeGuard_DoesNotClusterOrSuppressByClientFingerprint()
    {
        var guard = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "VoteBrigadeGuard.cs");

        guard.Should().NotContain("d.fingerprint",
            "a client-supplied fingerprint must not decide which votes are suppressed");
        guard.Should().NotContain("brigade_fingerprint_prefix");
        guard.Should().Contain("SuppressByIpBlockAsync", "server-observed network blocks remain a brigade signal");
    }

    [Fact]
    public void BrigadeCoordinatedDetector_DoesNotUseFingerprintConcentration()
    {
        var job = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "BrigadeCoordinatedDetectorJob.cs");

        job.Should().NotContain("fingerprint",
            "all official clients share one fingerprint prefix, so its concentration says nothing about coordination");
        job.Should().Contain("ip_concentration");
    }

    [Fact]
    public void LatestTrendScoreFunction_CountsDistinctDevicesNotFingerprints()
    {
        var latest = LatestFunctionBody("CREATE OR REPLACE FUNCTION refresh_trend_scores");

        latest.Should().NotContain("d.fingerprint",
            "COUNT(DISTINCT fingerprint) collapses every organic vote of a platform into one");
        latest.Should().Contain("THEN d.id END", "unique votes are counted per device");
        latest.Should().Contain("LEFT JOIN devices d ON d.id = v.device_id AND NOT d.is_banned",
            "banned devices must stay excluded");
        latest.Should().Contain("v.is_quarantined = FALSE");
        latest.Should().Contain("v.is_suppressed = FALSE");
    }

    [Fact]
    public void FingerprintSuppressedVotes_AreRestoredAndCountersRecomputed()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V106__client_fingerprint_signal_removal.sql");

        migration.Should().Contain("suppression_reason = 'brigade_fingerprint_prefix'");
        migration.Should().Contain("SET is_suppressed = FALSE");
        migration.Should().Contain("v2.voter_ip_block = v.voter_ip_block",
            "votes that the network-block rule would also have suppressed stay suppressed");
        migration.Should().Contain("vote_count_hakli");
        migration.Should().Contain("vote_count_haksiz");
    }

    // MigrationsRunner sırasıyla (sayısal sürüm, sonra ad) en son uygulanan tanımın gövdesi.
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
        bodyStart.Should().BeGreaterThan(start);
        bodyEnd.Should().BeGreaterThan(bodyStart);
        return latest[bodyStart..bodyEnd];
    }
}
