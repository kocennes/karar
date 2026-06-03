using Karar.Api.Models;
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
        NotificationTypes.ModerationResult or NotificationTypes.SystemAnnouncement
            or NotificationTypes.CrisisSupport => NotificationPriority.Critical,
        NotificationTypes.ReplyOnComment or NotificationTypes.Mention or NotificationTypes.Follow
            or NotificationTypes.VerdictMilestone or NotificationTypes.ViralPostOwner => NotificationPriority.High,
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

    // ─── Per-post / per-category fatigue guards ──────────────────────────────

    private static readonly TimeSpan PostCooldown = TimeSpan.FromHours(1);
    private static readonly TimeSpan CategoryCooldown = TimeSpan.FromMinutes(30);
    private const int CategoryCooldownLimit = 2;

    /// Returns false (suppress) when a push was already sent for this post within the last hour.
    /// Uses SETNX: sets the key on first call (allow), returns false if key exists (suppress).
    public async Task<bool> CheckPostCooldownAsync(
        Guid deviceId, Guid postId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var key = $"notif:post_cd:{deviceId:N}:{postId:N}";
        try
        {
            return await redis.GetDb().StringSetAsync(key, "1", PostCooldown, StackExchange.Redis.When.NotExists);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Post cooldown check failed for device {DeviceId} post {PostId}", deviceId, postId);
            return true;
        }
    }

    /// Maps a notification type to a broad category for fatigue tracking.
    public static string GetNotificationCategory(string type) => type switch
    {
        NotificationTypes.CommentOnPost or NotificationTypes.ReplyOnComment
            or NotificationTypes.Mention => "comments",
        NotificationTypes.VerdictMilestone or NotificationTypes.VerdictReminder
            or NotificationTypes.ViralPostOwner => "verdicts",
        NotificationTypes.TrendAlert or NotificationTypes.FollowNewPost => "trends",
        NotificationTypes.Follow => "social",
        NotificationTypes.WeeklyDigest => "digest",
        _ => "system",
    };

    /// Sliding-window category guard: max 2 pushes per category per 30 minutes.
    /// "system" and "digest" categories are exempt.
    public async Task<bool> CheckCategoryCooldownAsync(
        Guid deviceId, string type, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var category = GetNotificationCategory(type);
        if (category is "system" or "digest") return true;

        var key = $"notif:cat_cd:{deviceId:N}:{category}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cutoffMs = nowMs - (long)CategoryCooldown.TotalMilliseconds;
        var expSec = (long)CategoryCooldown.TotalSeconds + 60;

        const string script = """
            local key      = KEYS[1]
            local nowMs    = tonumber(ARGV[1])
            local cutoffMs = tonumber(ARGV[2])
            local limit    = tonumber(ARGV[3])
            local expSec   = tonumber(ARGV[4])

            redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoffMs)

            local count = redis.call('ZCARD', key)
            if count >= limit then return 0 end

            local member = tostring(nowMs) .. '-' .. tostring(count)
            redis.call('ZADD', key, nowMs, member)
            redis.call('EXPIRE', key, expSec)
            return 1
            """;

        try
        {
            var result = await redis.GetDb().ScriptEvaluateAsync(
                script,
                keys: new RedisKey[] { key },
                values: new RedisValue[] { nowMs, cutoffMs, CategoryCooldownLimit, expSec });
            return (int)result == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Category cooldown check failed for device {DeviceId} type {Type}", deviceId, type);
            return true;
        }
    }

    // ─── Sliding-window rate limit ───────────────────────────────────────────

    private const int SlidingWindowLimitDefault = 3;
    private static readonly TimeSpan SlidingWindowDuration = TimeSpan.FromMinutes(15);

    /// Sliding-window check: max 3 pushes per device per 15 minutes.
    /// Uses a Redis ZSET where each member is a unique score-based entry and old entries are pruned.
    /// Returns true (allow) when the device is under the limit; false (block) when exceeded.
    public async Task<bool> CheckSlidingWindowAsync(
        Guid deviceId,
        CancellationToken ct = default,
        int limit = SlidingWindowLimitDefault)
    {
        ct.ThrowIfCancellationRequested();

        var key = $"notif:sliding:{deviceId:N}";
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cutoffMs = nowMs - (long)SlidingWindowDuration.TotalMilliseconds;
        var windowSec = (long)SlidingWindowDuration.TotalSeconds + 60; // EXPIRE buffer

        const string script = """
            local key      = KEYS[1]
            local nowMs    = tonumber(ARGV[1])
            local cutoffMs = tonumber(ARGV[2])
            local limit    = tonumber(ARGV[3])
            local expSec   = tonumber(ARGV[4])

            redis.call('ZREMRANGEBYSCORE', key, '-inf', cutoffMs)

            local count = redis.call('ZCARD', key)
            if count >= limit then return 0 end

            -- Use nowMs + random suffix as unique member to avoid collisions
            local member = tostring(nowMs) .. '-' .. tostring(count)
            redis.call('ZADD', key, nowMs, member)
            redis.call('EXPIRE', key, expSec)
            return 1
            """;

        try
        {
            var result = await redis.GetDb().ScriptEvaluateAsync(
                script,
                keys: new RedisKey[] { key },
                values: new RedisValue[] { nowMs, cutoffMs, limit, windowSec });

            return (int)result == 1;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sliding-window rate limit check failed for device {DeviceId}", deviceId);
            return true; // fail-open: don't block on Redis errors
        }
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
