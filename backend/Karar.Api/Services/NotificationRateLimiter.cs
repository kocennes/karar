using StackExchange.Redis;

namespace Karar.Api.Services;

public enum NotificationPriority
{
    Normal,
    High,
    Critical,
}

public sealed class NotificationRateLimiter(
    RedisService redis,
    IConfiguration configuration,
    ILogger<NotificationRateLimiter> logger)
{
    private static readonly TimeSpan TurkeyOffset = TimeSpan.FromHours(3);
    private readonly bool _ramadanMode = configuration.GetValue("Notifications:RamadanMode", false);

    public async Task<bool> CanSendAsync(
        Guid deviceId,
        NotificationPriority priority,
        DateTimeOffset deviceCreatedAt,
        CancellationToken ct = default)
    {
        if (priority == NotificationPriority.Critical)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        if (IsQuietHour(now, _ramadanMode))
        {
            return false;
        }

        var dailyLimit = GetDailyLimit(deviceCreatedAt, now);
        var dailyKey = $"notif:daily:{deviceId:N}:{ToTurkeyDate(now):yyyyMMdd}";
        var hourlyKey = $"notif:hourly:{deviceId:N}:{ToTurkeyHour(now):yyyyMMddHH}";

        const string script = """
            local dailyKey = KEYS[1]
            local hourlyKey = KEYS[2]
            local dailyLimit = tonumber(ARGV[1])
            local hourlyLimit = tonumber(ARGV[2])
            local dailyExpiry = tonumber(ARGV[3])
            local hourlyExpiry = tonumber(ARGV[4])

            local dailyCount = tonumber(redis.call('GET', dailyKey) or '0')
            local hourlyCount = tonumber(redis.call('GET', hourlyKey) or '0')

            if dailyCount >= dailyLimit or hourlyCount >= hourlyLimit then
                return 0
            end

            dailyCount = redis.call('INCR', dailyKey)
            if dailyCount == 1 then redis.call('EXPIRE', dailyKey, dailyExpiry) end

            hourlyCount = redis.call('INCR', hourlyKey)
            if hourlyCount == 1 then redis.call('EXPIRE', hourlyKey, hourlyExpiry) end

            return 1
        """;

        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await redis.GetDb().ScriptEvaluateAsync(
                script,
                keys: new RedisKey[] { dailyKey, hourlyKey },
                values: new RedisValue[] { dailyLimit, 1, 26 * 60 * 60, 2 * 60 * 60 });

            return (int)result == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Notification rate limit failed for device {DeviceId}", deviceId);
            return true;
        }
    }

    public static NotificationPriority GetPriority(string type) => type switch
    {
        "moderation_result" or "system_announcement" => NotificationPriority.Critical,
        "reply_on_comment" or "mention" or "verdict_milestone" or "viral_post_owner" => NotificationPriority.High,
        _ => NotificationPriority.Normal,
    };

    public static bool IsQuietHour(DateTimeOffset utcNow, bool ramadanMode = false)
    {
        var localTime = ToTurkeyTime(utcNow).TimeOfDay;
        if (ramadanMode)
        {
            var windowStart = new TimeSpan(21, 0, 0);
            var windowEnd = new TimeSpan(0, 30, 0);
            var isRamadanWindow = localTime >= windowStart || localTime <= windowEnd;
            return !isRamadanWindow;
        }

        return localTime.Hours is >= 23 or < 8;
    }

    public static int GetDailyLimit(DateTimeOffset deviceCreatedAt, DateTimeOffset utcNow)
    {
        return utcNow - deviceCreatedAt < TimeSpan.FromHours(48) ? 1 : 2;
    }

    private static DateTimeOffset ToTurkeyDate(DateTimeOffset utcNow)
    {
        var local = ToTurkeyTime(utcNow);
        return new DateTimeOffset(local.Year, local.Month, local.Day, 0, 0, 0, TurkeyOffset);
    }

    private static DateTimeOffset ToTurkeyHour(DateTimeOffset utcNow)
    {
        var local = ToTurkeyTime(utcNow);
        return new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, TurkeyOffset);
    }

    private static DateTimeOffset ToTurkeyTime(DateTimeOffset utcNow)
    {
        return utcNow.ToUniversalTime().Add(TurkeyOffset);
    }
}
