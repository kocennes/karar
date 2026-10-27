namespace Karar.Api.Services;

public sealed class ReportThresholdService(IConfiguration? configuration = null)
{
    private readonly double _postThreshold =
        configuration?.GetValue<double?>("Moderation:WeightedReportThresholds:Post") ?? 5.0;
    private readonly double _commentThreshold =
        configuration?.GetValue<double?>("Moderation:WeightedReportThresholds:Comment") ?? 3.0;
    private readonly double _criticalThreshold =
        configuration?.GetValue<double?>("Moderation:WeightedReportThresholds:Critical") ?? 3.0;

    public ReportThresholdDecision Evaluate(
        string targetType,
        double weightedReporterCount,
        double weightedCriticalCount
    )
    {
        if (weightedCriticalCount >= _criticalThreshold)
        {
            return ReportThresholdDecision.AutoHide(
                "critical",
                "Kritik kategoride 3 veya daha fazla rapor alındı."
            );
        }

        if (targetType == "post" && weightedReporterCount >= _postThreshold)
        {
            return ReportThresholdDecision.AutoHide(
                "high",
                "Post 5 veya daha fazla farklı cihazdan raporlandı."
            );
        }

        if (targetType == "comment" && weightedReporterCount >= _commentThreshold)
        {
            return ReportThresholdDecision.AutoHide(
                "medium",
                "Yorum 3 veya daha fazla farklı cihazdan raporlandı."
            );
        }

        return ReportThresholdDecision.KeepPending();
    }

    // Bekleyen raporları ağırlıklandırıp eşik kararını verir.
    // Yalnızca yerleşik cihazların (ReportAbuseProtectionService.EstablishedReporterMinAge) raporları
    // otomatik gizlemeye sayılır: cihaz kaydı hesapsız ve ucuz olduğundan birkaç taze cihazla
    // (farklı ağlardan bile) içerik 'under_review'a alınıp sansürlenebiliyordu. Taze cihazların
    // raporları tek başına eşiği aşacak kadar birikirse içerik gizlenmez, admin uyarısı istenir.
    public ReportThresholdDecision EvaluateReporters(
        string targetType,
        IReadOnlyCollection<ReporterSnapshot> reporters
    )
    {
        var established = reporters
            .Where(r => ReportAbuseProtectionService.IsEstablishedReporter(r.DeviceAge))
            .ToList();

        var decision = EvaluateWeighted(targetType, established);
        if (decision.ShouldAutoHide)
        {
            return decision;
        }

        var unfiltered = EvaluateWeighted(targetType, reporters.ToList());
        return unfiltered.ShouldAutoHide
            ? ReportThresholdDecision.HoldForAdminReview(unfiltered.Priority, unfiltered.Reason)
            : decision;
    }

    // Her raporcu diğerleriyle (kendisi hariç) karşılaştırılır: aynı ağ bloğunda başka raporcu
    // varsa koordineli küme şüphesiyle düşük ağırlık alır; sonuç itibar ağırlığıyla çarpılır.
    private ReportThresholdDecision EvaluateWeighted(string targetType, IReadOnlyList<ReporterSnapshot> reporters)
    {
        var networks = reporters.Select(r => (r.Fingerprint, r.IpBlock)).ToList();
        var weightedReporters = 0.0;
        var weightedCritical = 0.0;

        for (var i = 0; i < reporters.Count; i++)
        {
            var reporter = reporters[i];
            var others = networks.Where((_, j) => j != i);
            var weight =
                ReportAbuseProtectionService.ComputeReportWeight(reporter.Fingerprint, reporter.IpBlock, others)
                * ReporterReputationService.ComputeWeight(reporter.AccurateCount, reporter.TotalCount);

            weightedReporters += weight;
            if (IsCriticalReason(reporter.Reason))
            {
                weightedCritical += weight;
            }
        }

        return Evaluate(targetType, weightedReporters, weightedCritical);
    }

    // Returns the weighted count of independent reporters.
    // Within each IP block, only the highest-weight reporter counts.
    // Reporters without IP data contribute their weight directly.
    public double CountWeightedIndependentReporters(IEnumerable<ReportSignal> reports)
    {
        var signals = reports.ToList();
        if (signals.Count == 0) return 0;

        // Deduplicate by fingerprint — same device = same person, keep max weight
        var byFingerprint = signals
            .Where(r => !string.IsNullOrWhiteSpace(r.Fingerprint))
            .GroupBy(r => r.Fingerprint, StringComparer.Ordinal)
            .Select(g => new
            {
                Weight = g.Max(r => r.Weight),
                IpBlock = g.Select(r => r.IpBlock).FirstOrDefault(b => !string.IsNullOrWhiteSpace(b))
            })
            .ToList();

        if (byFingerprint.Count == 0) return 0;

        var hasIpData = byFingerprint.Any(r => r.IpBlock != null);
        if (!hasIpData)
        {
            return byFingerprint.Sum(r => r.Weight);
        }

        // Within each IP block keep only the highest-weight reporter to prevent brigade amplification.
        // Fingerprints without an IP block each contribute their weight independently.
        var withIp = byFingerprint
            .Where(r => r.IpBlock != null)
            .GroupBy(r => r.IpBlock!, StringComparer.Ordinal)
            .Sum(g => g.Max(r => r.Weight));

        var withoutIp = byFingerprint
            .Where(r => r.IpBlock == null)
            .Sum(r => r.Weight);

        return withIp + withoutIp;
    }

    public bool IsCriticalReason(string reason) => reason is "self_harm" or "illegal";
}

public sealed record ReportSignal(string Fingerprint, string? IpBlock, string Reason, double Weight = 1.0);

// Bir hedefteki tek bir bekleyen raporun değerlendirme girdisi.
public sealed record ReporterSnapshot(
    string Fingerprint,
    string? IpBlock,
    string Reason,
    int AccurateCount,
    int TotalCount,
    TimeSpan DeviceAge
);

public sealed record ReportThresholdDecision(
    bool ShouldAutoHide,
    string Priority,
    string? Reason,
    bool NeedsAdminAttention = false
)
{
    public static ReportThresholdDecision AutoHide(string priority, string reason) =>
        new(true, priority, reason);

    public static ReportThresholdDecision KeepPending() =>
        new(false, "low", null);

    // İçerik yayında kalır; eşik yalnızca yeni cihazların raporlarıyla aşıldığı için admin uyarılır.
    public static ReportThresholdDecision HoldForAdminReview(string priority, string? reason) =>
        new(false, priority, reason, NeedsAdminAttention: true);
}
