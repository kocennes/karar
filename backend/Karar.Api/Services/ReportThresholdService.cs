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

public sealed record ReportThresholdDecision(
    bool ShouldAutoHide,
    string Priority,
    string? Reason
)
{
    public static ReportThresholdDecision AutoHide(string priority, string reason) =>
        new(true, priority, reason);

    public static ReportThresholdDecision KeepPending() =>
        new(false, "low", null);
}
