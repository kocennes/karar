namespace Karar.Api.Services;

public sealed class ReportAbuseProtectionService
{
    private readonly Func<string, string, int, TimeSpan, Task<bool>> _isAllowed;

    private const int ReportHourlyLimit = 10;
    private static readonly TimeSpan ReportWindow = TimeSpan.FromHours(1);

    public ReportAbuseProtectionService(RedisService redis) => _isAllowed = redis.IsAllowedAsync;

    // Test constructor — allows injecting an in-memory or fault-injecting rate limiter without Redis.
    internal ReportAbuseProtectionService(Func<string, string, int, TimeSpan, Task<bool>> isAllowed)
        => _isAllowed = isAllowed;

    // Device-based sliding window: cihaz başına saatte max 10 rapor.
    // Hata durumunda fail-open (Redis'e ulaşılamazsa isteğe izin verilir).
    public async Task<(bool IsAllowed, int RetryAfterSeconds)> CheckDeviceRateLimitAsync(Guid deviceId)
    {
        var allowed = await _isAllowed(
            "report-device",
            deviceId.ToString("N"),
            ReportHourlyLimit,
            ReportWindow
        );
        return (allowed, allowed ? 0 : (int)ReportWindow.TotalSeconds);
    }

    // Rapor ağırlığı hesapla — koordineli saldırı koruması.
    //   1.0 → benzersiz fingerprint VE benzersiz /24 subnet (bağımsız reporter)
    //   0.3 → mevcut bir reporter ile aynı /24 subnet (koordineli rapor şüphesi)
    //   0.1 → mevcut bir reporter ile aynı fingerprint (Sybil guard; DB UNIQUE kısıtı bunu engeller)
    public static double ComputeReportWeight(
        string reporterFingerprint,
        string? reporterIpBlock,
        IEnumerable<(string Fingerprint, string? IpBlock)> existingReporters)
    {
        var existing = existingReporters.ToList();
        if (existing.Count == 0)
            return 1.0;

        bool sameFingerprint = existing.Any(r =>
            string.Equals(r.Fingerprint, reporterFingerprint, StringComparison.Ordinal));

        if (sameFingerprint)
            return 0.1;

        if (reporterIpBlock is not null)
        {
            bool sameSubnet = existing.Any(r =>
                r.IpBlock is not null &&
                string.Equals(r.IpBlock, reporterIpBlock, StringComparison.Ordinal));

            if (sameSubnet)
                return 0.3;
        }

        return 1.0;
    }
}
