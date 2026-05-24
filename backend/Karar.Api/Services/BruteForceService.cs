using StackExchange.Redis;

namespace Karar.Api.Services;

// Cihaz kimliği (device fingerprint veya IP) bazlı başarısız giriş takibi.
// 10 başarısız deneme → 15 dakika lockout (Redis'te saklanır, restart'ta kaybolmaz).
public sealed class BruteForceService
{
    private const int MaxAttempts = 10;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    private readonly IDatabase _db;

    public BruteForceService(RedisService redis)
    {
        _db = redis.GetDb();
    }

    private static RedisKey AttemptsKey(string identity) => $"bf:attempts:{identity}";
    private static RedisKey LockoutKey(string identity) => $"bf:lockout:{identity}";

    public async Task<bool> IsLockedOutAsync(string identity)
    {
        return await _db.KeyExistsAsync(LockoutKey(identity));
    }

    // Başarısız denemeyi kaydeder. Lockout'a girildiyse true döner.
    public async Task<(bool IsLockedOut, int AttemptsRemaining)> RecordFailedAttemptAsync(string identity)
    {
        var key = AttemptsKey(identity);
        var attempts = await _db.StringIncrementAsync(key);
        if (attempts == 1)
        {
            await _db.KeyExpireAsync(key, AttemptWindow);
        }

        if (attempts >= MaxAttempts)
        {
            await _db.StringSetAsync(LockoutKey(identity), "1", LockoutDuration);
            await _db.KeyDeleteAsync(key);
            return (true, 0);
        }

        return (false, (int)(MaxAttempts - attempts));
    }

    // Başarılı girişte sayacı temizle.
    public async Task ClearAsync(string identity)
    {
        await _db.KeyDeleteAsync(AttemptsKey(identity));
        await _db.KeyDeleteAsync(LockoutKey(identity));
    }

    public async Task<int> GetFailedAttemptCountAsync(string identity)
    {
        var val = await _db.StringGetAsync(AttemptsKey(identity));
        return val.TryParse(out int n) ? n : 0;
    }

    // Başarısız deneme sayısına göre progressive delay süresi (ms).
    // 3. denemeden itibaren 2^(n-2) × 100ms, max 5 saniye.
    public static int ComputeDelayMs(int failedAttempts) =>
        failedAttempts < 3 ? 0 : (int)Math.Min(Math.Pow(2, failedAttempts - 2) * 100, 5000);

    // IP + device + endpoint kombinasyonu icin identity olusturur.
    public static string IdentityFor(string ip, string endpoint, Guid? deviceId = null)
    {
        var devicePart = deviceId?.ToString("N") ?? "unknown-device";
        return $"{ip}:{devicePart}:{endpoint}";
    }
}
