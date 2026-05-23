namespace Karar.Api.Services;

public static class CacheKeys
{
    // Feed
    public static string FeedTrending(int page) => $"feed:trending:p{page}";
    public static string FeedNew(int page) => $"feed:new:p{page}";
    public static string FeedCategory(int catId, string sort, int page) => $"feed:cat:{catId}:{sort}:p{page}";

    // Post
    public static string Post(Guid id) => $"post:{id}";

    // Cihaz ban durumu (5 dk TTL — middleware cache)
    public static string DeviceBan(Guid deviceId) => $"ban:device:{deviceId}";

    // Kategoriler
    public const string Categories = "categories:all";
}
