using System.Text.Json;
using StackExchange.Redis;

namespace Karar.Api.Services;

public sealed class RedisService : IDisposable
{
    private readonly ConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly ILogger<RedisService> _logger;

    public RedisService(IConfiguration configuration, ILogger<RedisService> logger)
    {
        _logger = logger;
        var connectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is missing.");

        var options = ConfigurationOptions.Parse(connectionString);

        // Phase 5: Production hardening — AUTH and TLS support
        var password = configuration["Redis:Password"];
        if (!string.IsNullOrEmpty(password))
        {
            options.Password = password;
        }

        if (configuration.GetValue<bool>("Redis:Ssl"))
        {
            options.Ssl = true;
            // Memorystore typically uses a self-signed cert for transit encryption
            // We allow any certificate in the internal VPC context for simplicity,
            // or we could point to a root cert if strictly needed.
            options.CertificateValidation += (sender, cert, chain, errors) => true;
        }

        _redis = ConnectionMultiplexer.Connect(options);
        _db = _redis.GetDatabase();
    }

    public IDatabase GetDb() => _db;

    // Cache-aside okuma. Redis erişilemezse null döner (DB fallback için).
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var value = await _db.StringGetAsync(key);
            if (!value.HasValue)
            {
                await IncrementMetricAsync("redis:misses");
                return null;
            }
            await IncrementMetricAsync("redis:hits");
            return JsonSerializer.Deserialize<T>(value!);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis GET başarısız: {Key}", key);
            return null;
        }
    }

    private async Task IncrementMetricAsync(string key)
    {
        try
        {
            await _db.StringIncrementAsync(key);
        }
        catch { }
    }

    public async Task<(long Hits, long Misses)> GetCacheMetricsAsync()
    {
        try
        {
            var hits = (long)await _db.StringGetAsync("redis:hits");
            var misses = (long)await _db.StringGetAsync("redis:misses");
            return (hits, misses);
        }
        catch
        {
            return (0, 0);
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan expiry)
    {
        try
        {
            var json = JsonSerializer.Serialize(value);
            await _db.StringSetAsync(key, json, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis SET başarısız: {Key}", key);
        }
    }

    public async Task DeleteAsync(params string[] keys)
    {
        try
        {
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            await _db.KeyDeleteAsync(redisKeys);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis DEL başarısız: {Keys}", string.Join(',', keys));
        }
    }

    // Pattern ile toplu silme — SCAN kullanarak keyspace'i yavaşça tarar, tümünü RAM'e yüklemez.
    public async Task DeleteByPatternAsync(string pattern)
    {
        try
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server is null) return;

            var batch = new List<RedisKey>(100);
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                batch.Add(key);
                if (batch.Count >= 100)
                {
                    await _db.KeyDeleteAsync(batch.ToArray());
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
                await _db.KeyDeleteAsync(batch.ToArray());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis pattern DEL başarısız: {Pattern}", pattern);
        }
    }

    // Distributed Sliding Window Rate Limiting (Redis Lua)
    public async Task<bool> IsAllowedAsync(string endpoint, string identity, int limit, TimeSpan window)
    {
        var key = $"rl:{endpoint}:{identity}";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs = (long)window.TotalMilliseconds;

        const string script = @"
            local key = KEYS[1]
            local now = tonumber(ARGV[1])
            local window = tonumber(ARGV[2])
            local limit = tonumber(ARGV[3])

            redis.call('ZREMRANGEBYSCORE', key, 0, now - window)
            local count = redis.call('ZCARD', key)

            if count < limit then
                redis.call('ZADD', key, now, now)
                redis.call('EXPIRE', key, math.ceil(window / 1000) + 1)
                return 1
            end
            return 0
        ";

        try
        {
            var result = await _db.ScriptEvaluateAsync(script,
                keys: new RedisKey[] { key },
                values: new RedisValue[] { now, windowMs, limit });

            return (int)result == 1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rate limit kontrolü başarısız: {Key}", key);
            return true; // Hata durumunda isteğe izin ver (fail-open)
        }
    }

    public async Task MarkPostDirtyAsync(Guid postId)
    {
        try
        {
            await _db.SortedSetAddAsync("posts:dirty", postId.ToString(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis dirty mark başarısız: {PostId}", postId);
        }
    }

    // "İlgilenmiyorum" — cihaz başına 30 günlük sorted set (score = unix timestamp)
    private static string NotInterestedKey(Guid deviceId) => $"device:notinterested:{deviceId}";
    private static readonly TimeSpan NotInterestedWindow = TimeSpan.FromDays(30);

    public async Task MarkNotInterestedAsync(Guid deviceId, Guid postId)
    {
        try
        {
            var key = NotInterestedKey(deviceId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await _db.SortedSetAddAsync(key, postId.ToString(), now);
            await _db.KeyExpireAsync(key, NotInterestedWindow);

            // Prune entries older than 30 days
            var cutoff = now - (long)NotInterestedWindow.TotalSeconds;
            await _db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, cutoff);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotInterested mark başarısız: {DeviceId}/{PostId}", deviceId, postId);
        }
    }

    public async Task<HashSet<Guid>> GetNotInterestedPostsAsync(Guid deviceId)
    {
        try
        {
            var key = NotInterestedKey(deviceId);
            var cutoff = DateTimeOffset.UtcNow.Subtract(NotInterestedWindow).ToUnixTimeSeconds();
            var members = await _db.SortedSetRangeByScoreAsync(key, cutoff, double.PositiveInfinity);
            var result = new HashSet<Guid>(members.Length);
            foreach (var m in members)
            {
                if (Guid.TryParse(m, out var id)) result.Add(id);
            }
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NotInterested get başarısız: {DeviceId}", deviceId);
            return [];
        }
    }

    public async Task<List<Guid>> GetDirtyPostsAsync(int count)
    {
        try
        {
            var members = await _db.SortedSetPopAsync("posts:dirty", count);
            return members.Select(m => Guid.Parse(m.Element!)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis dirty posts çekme başarısız");
            return new List<Guid>();
        }
    }

    // Trend velocity EWMA — smoothed vote count per post (anti-gaming)
    // Key stores: "prevVotes|ewmaVelocity" as floats
    private static string TrendVelocityKey(Guid postId) => $"post:{postId}:trend_velocity";

    public async Task<(int PrevVotes, double EwmaVelocity)> GetTrendVelocityAsync(Guid postId)
    {
        try
        {
            var val = await _db.StringGetAsync(TrendVelocityKey(postId));
            if (!val.HasValue) return (0, 0);
            var parts = ((string)val!).Split('|');
            if (parts.Length == 2
                && int.TryParse(parts[0], out var prev)
                && double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var ewma))
            {
                return (prev, ewma);
            }
            return (0, 0);
        }
        catch
        {
            return (0, 0);
        }
    }

    public async Task SetTrendVelocityAsync(Guid postId, int currentVotes, double newEwma)
    {
        try
        {
            var val = $"{currentVotes}|{newEwma.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            await _db.StringSetAsync(TrendVelocityKey(postId), val, TimeSpan.FromHours(48));
        }
        catch { }
    }

    // Post impression tracking — prevents showing the same post >2 times in 24h per device.
    // Key: device:{deviceId}:impressions  Value: sorted set, score=unix_ts, member=postId
    private static string ImpressionsKey(Guid deviceId) => $"device:{deviceId}:impressions";
    private static readonly TimeSpan ImpressionsWindow = TimeSpan.FromHours(24);

    public async Task RecordImpressionsAsync(Guid deviceId, IEnumerable<Guid> postIds)
    {
        try
        {
            var key = ImpressionsKey(deviceId);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var entries = postIds
                .Select(id => new SortedSetEntry(id.ToString(), now))
                .ToArray();
            if (entries.Length == 0) return;
            // ZADD with NX: only increment new impressions; use ZINCRBY for existing ones
            await _db.SortedSetAddAsync(key, entries, SortedSetWhen.NotExists);
            // Increment view count for already-seen posts by adding again (without NX)
            // We use score as timestamp, so we overwrite score for re-impressions
            // But we track count separately via a hash
            var hashKey = $"device:{deviceId}:view_counts";
            var batch = _db.CreateBatch();
            var tasks = new List<Task>();
            foreach (var postId in postIds)
                tasks.Add(batch.HashIncrementAsync(hashKey, postId.ToString(), 1));
            batch.Execute();
            await Task.WhenAll(tasks);
            await _db.KeyExpireAsync(hashKey, ImpressionsWindow);
            // Prune old entries from sorted set
            var cutoff = now - (long)ImpressionsWindow.TotalSeconds;
            await _db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, cutoff);
            await _db.KeyExpireAsync(key, ImpressionsWindow);
        }
        catch { }
    }

    public async Task<HashSet<Guid>> GetOverImposedPostsAsync(Guid deviceId, int maxImpressions = 2)
    {
        try
        {
            var hashKey = $"device:{deviceId}:view_counts";
            var entries = await _db.HashGetAllAsync(hashKey);
            var result = new HashSet<Guid>();
            foreach (var entry in entries)
            {
                if ((long)entry.Value >= maxImpressions && Guid.TryParse(entry.Name, out var id))
                    result.Add(id);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    // City-level trending sorted sets: trending:city:{city} → post scores with 24h TTL
    private static string CityTrendingKey(string city) => $"trending:city:{city.ToLowerInvariant().Replace(' ', '_')}";
    private static readonly TimeSpan CityTrendingTtl = TimeSpan.FromHours(24);

    public async Task UpdateCityTrendingAsync(string city, Guid postId, double trendScore)
    {
        if (string.IsNullOrWhiteSpace(city)) return;
        try
        {
            var key = CityTrendingKey(city);
            await _db.SortedSetAddAsync(key, postId.ToString(), trendScore);
            await _db.KeyExpireAsync(key, CityTrendingTtl);
        }
        catch { }
    }

    public async Task<IReadOnlyList<Guid>> GetCityTrendingPostIdsAsync(string city, int count = 20)
    {
        try
        {
            var key = CityTrendingKey(city);
            var entries = await _db.SortedSetRangeByRankAsync(key, 0, count - 1, Order.Descending);
            var result = new List<Guid>(entries.Length);
            foreach (var e in entries)
            {
                if (Guid.TryParse(e, out var id)) result.Add(id);
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    // Rising comment slot — tracks fastest-rising comment per post within a 10-min window.
    // ZINCRBY increments score; TTL naturally resets the window when no new upvotes come in.
    private static string RisingCommentsKey(Guid postId) => $"rising:{postId}:comments";
    private static readonly TimeSpan RisingWindow = TimeSpan.FromMinutes(10);
    private const int RisingMinUpvotes = 2;

    public async Task IncrementRisingCommentAsync(Guid postId, Guid commentId)
    {
        try
        {
            var key = RisingCommentsKey(postId);
            await _db.SortedSetIncrementAsync(key, commentId.ToString(), 1);
            await _db.KeyExpireAsync(key, RisingWindow);
        }
        catch { }
    }

    public async Task<Guid?> GetRisingCommentAsync(Guid postId)
    {
        try
        {
            var key = RisingCommentsKey(postId);
            var entries = await _db.SortedSetRangeByScoreWithScoresAsync(
                key, RisingMinUpvotes, double.PositiveInfinity, Exclude.None, Order.Descending, 0, 1);
            if (entries.Length == 0) return null;
            return Guid.TryParse(entries[0].Element, out var id) ? id : null;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _redis.Dispose();
}
