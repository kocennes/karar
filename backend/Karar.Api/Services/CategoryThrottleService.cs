namespace Karar.Api.Services;

public sealed class CategoryThrottleService(RedisService redis, ILogger<CategoryThrottleService> logger)
{
    private static string Key(int categoryId) => $"category:throttle:{categoryId}";

    public async Task<bool> IsThrottledAsync(int categoryId)
    {
        try
        {
            return await redis.GetDb().KeyExistsAsync(Key(categoryId));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kategori throttle kontrol hatası: {CategoryId}", categoryId);
            return false;
        }
    }

    public async Task SetThrottledAsync(int categoryId, TimeSpan duration, string reason)
    {
        try
        {
            await redis.GetDb().StringSetAsync(Key(categoryId), reason, duration);
            logger.LogWarning(
                "Kategori throttled: {CategoryId}, süre: {Duration:.0}h, sebep: {Reason}",
                categoryId, duration.TotalHours, reason);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kategori throttle set hatası: {CategoryId}", categoryId);
        }
    }

    public Task SetThrottledUntilAsync(int categoryId, DateTime untilUtc, string reason)
    {
        var duration = untilUtc - DateTime.UtcNow;
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(1);
        }

        return SetThrottledAsync(categoryId, duration, reason);
    }

    public async Task ClearThrottleAsync(int categoryId)
    {
        try
        {
            await redis.GetDb().KeyDeleteAsync(Key(categoryId));
            logger.LogInformation("Kategori throttle kaldırıldı: {CategoryId}", categoryId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Kategori throttle clear hatası: {CategoryId}", categoryId);
        }
    }

    public async Task<CategoryThrottleStatus> GetStatusAsync(int categoryId)
    {
        try
        {
            var db = redis.GetDb();
            var key = Key(categoryId);
            var reason = (string?)await db.StringGetAsync(key);
            if (reason is null)
                return new CategoryThrottleStatus(false, null, null);

            var ttl = await db.KeyTimeToLiveAsync(key);
            return new CategoryThrottleStatus(true, reason, ttl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Kategori throttle status hatası: {CategoryId}", categoryId);
            return new CategoryThrottleStatus(false, null, null);
        }
    }
}

public record CategoryThrottleStatus(bool IsThrottled, string? Reason, TimeSpan? Remaining);
