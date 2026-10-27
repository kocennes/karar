using Karar.Api.Services;

namespace Karar.Api.Common.Middleware;

/// <summary>
/// Her gerçek kullanıcı isteğinde <see cref="ActivityTracker.MarkRequest"/> çağırır.
/// Sağlık/uptime ping'leri, crawler altyapısı (robots, sitemap, .well-known) ve OG kabuğuna gelen
/// bot istekleri etkinlik sayılmaz; aksi halde 5 dakikalık uptime ping'i işçileri ve Neon'u
/// sürekli uyanık tutardı. Pipeline'ı asla kısa devre etmez.
/// </summary>
public sealed class ActivityTrackingMiddleware(RequestDelegate next, ActivityTracker activity)
{
    private static readonly string[] IgnoredPrefixes =
    [
        "/health",
        "/metrics",
        "/.well-known",
        "/robots.txt",
        "/sitemap",
    ];

    private static readonly PathString OgShellPrefix = new("/posts");

    public Task InvokeAsync(HttpContext context)
    {
        if (CountsAsUserActivity(context.Request.Path, context.Request.Headers.UserAgent))
            activity.MarkRequest();

        return next(context);
    }

    public static bool CountsAsUserActivity(PathString path, string? userAgent)
    {
        var value = path.Value;
        if (string.IsNullOrEmpty(value)) return true;

        foreach (var prefix in IgnoredPrefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // OG kabuğu: bot yalnızca önizleme çekiyor, kullanıcı değil. İnsan ziyaretçi web'e yönlendirilir ve sayılır.
        if (path.StartsWithSegments(OgShellPrefix) && CrawlerDetector.IsCrawler(userAgent))
            return false;

        return true;
    }
}
