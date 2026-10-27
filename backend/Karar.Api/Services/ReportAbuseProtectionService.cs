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
    //   1.0 → benzersiz /24 (IPv6: /64) ağ bloğu (bağımsız reporter)
    //   0.3 → mevcut bir reporter ile aynı ağ bloğu (koordineli rapor şüphesi)
    // Fingerprint bilinçli olarak sinyal değildir: istemci tarafından gönderilir, resmi istemci
    // her kurulumda aynı sabit değeri yollar ve bot rastgele üretebilir. Cihaz başına tek rapor
    // zaten reports (reporter_device_id, target_type, target_id) UNIQUE kısıtıyla sağlanır.
    public static double ComputeReportWeight(
        string reporterFingerprint,
        string? reporterIpBlock,
        IEnumerable<(string Fingerprint, string? IpBlock)> existingReporters)
    {
        var existing = existingReporters.ToList();
        if (existing.Count == 0)
            return 1.0;

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
