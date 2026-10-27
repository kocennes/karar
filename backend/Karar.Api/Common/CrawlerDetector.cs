namespace Karar.Api.Common;

/// <summary>
/// Sosyal önizleme ve arama botlarını User-Agent'tan tanır.
/// OG kabuğu (/posts/{id|slug}) ve boşta kalma kapısı aynı listeyi kullanır.
/// </summary>
public static class CrawlerDetector
{
    private static readonly string[] Signatures =
    [
        "facebookexternalhit",
        "Twitterbot",
        "WhatsApp",
        "Googlebot",
        "LinkedInBot",
        "Slackbot",
        "TelegramBot",
        "Discordbot",
    ];

    public static bool IsCrawler(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return false;

        foreach (var signature in Signatures)
        {
            if (userAgent.Contains(signature, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
