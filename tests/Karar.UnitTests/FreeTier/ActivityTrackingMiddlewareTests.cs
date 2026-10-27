using FluentAssertions;
using Karar.Api.Common.Middleware;
using Karar.Api.Services;
using Microsoft.AspNetCore.Http;

namespace Karar.UnitTests.FreeTier;

public sealed class ActivityTrackingMiddlewareTests
{
    private const string BrowserUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/health/version")]
    [InlineData("/health/slo")]
    [InlineData("/health/slo/alerts")]
    [InlineData("/metrics")]
    [InlineData("/.well-known/apple-app-site-association")]
    [InlineData("/.well-known/assetlinks.json")]
    [InlineData("/.well-known/jwks.json")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    [InlineData("/sitemap-main.xml")]
    [InlineData("/sitemap-posts-1.xml")]
    public void HealthAndCrawlerInfrastructurePaths_DoNotCountAsUserActivity(string path)
    {
        ActivityTrackingMiddleware.CountsAsUserActivity(new PathString(path), BrowserUserAgent)
            .Should().BeFalse("{0} is hit by uptime pingers and crawlers, not by users", path);
        ActivityTrackingMiddleware.CountsAsUserActivity(new PathString(path), null)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("/HEALTH")]
    [InlineData("/Health/Live")]
    [InlineData("/Robots.txt")]
    public void ExcludedPaths_AreMatchedCaseInsensitively(string path)
    {
        ActivityTrackingMiddleware.CountsAsUserActivity(new PathString(path), BrowserUserAgent)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)")]
    [InlineData("Twitterbot/1.0")]
    [InlineData("WhatsApp/2.23.20.0")]
    [InlineData("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)")]
    [InlineData("LinkedInBot/1.0")]
    [InlineData("Slackbot-LinkExpanding 1.0")]
    [InlineData("TelegramBot (like TwitterBot)")]
    [InlineData("Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)")]
    public void CrawlerHitOnOgShellRoute_DoesNotCountAsUserActivity(string crawlerUserAgent)
    {
        ActivityTrackingMiddleware.CountsAsUserActivity(
                new PathString("/posts/3fa85f64-5717-4562-b3fc-2c963f66afa6"), crawlerUserAgent)
            .Should().BeFalse();
        ActivityTrackingMiddleware.CountsAsUserActivity(
                new PathString("/posts/3fa85f64-komsu-gurultusu"), crawlerUserAgent)
            .Should().BeFalse();
    }

    [Fact]
    public void HumanHitOnOgShellRoute_CountsAsUserActivity()
    {
        ActivityTrackingMiddleware.CountsAsUserActivity(
                new PathString("/posts/3fa85f64-komsu-gurultusu"), BrowserUserAgent)
            .Should().BeTrue("a person opening a shared link is real traffic (they get redirected to the web app)");
    }

    [Theory]
    [InlineData("/api/v1/posts")]
    [InlineData("/api/v1/posts/3fa85f64-5717-4562-b3fc-2c963f66afa6/vote")]
    [InlineData("/api/v1/devices/register")]
    [InlineData("/api/v1/categories")]
    [InlineData("/api/v1/admin/categories/health")]
    [InlineData("/api/v1/admin/auth/login")]
    [InlineData("/")]
    public void ApiPaths_CountAsUserActivity(string path)
    {
        ActivityTrackingMiddleware.CountsAsUserActivity(new PathString(path), BrowserUserAgent)
            .Should().BeTrue();
        ActivityTrackingMiddleware.CountsAsUserActivity(new PathString(path), null)
            .Should().BeTrue("the app's own HTTP client does not always send a User-Agent");
    }

    [Fact]
    public void CrawlerUserAgent_OnApiPath_StillCountsAsUserActivity()
    {
        // The crawler exemption is scoped to the OG shell routes only; the API itself is not crawler-only.
        ActivityTrackingMiddleware.CountsAsUserActivity(new PathString("/api/v1/posts"), "Twitterbot/1.0")
            .Should().BeTrue();
    }

    [Fact]
    public async Task Middleware_MarksRequest_OnlyForUserTraffic()
    {
        var clock = new ActivityTrackerTests.ManualTimeProvider();
        var tracker = new ActivityTracker(enabled: true, idleAfter: TimeSpan.FromMinutes(10), clock);
        clock.Advance(TimeSpan.FromHours(1));
        tracker.IsIdle().Should().BeTrue();

        var nextCalls = 0;
        var middleware = new ActivityTrackingMiddleware(_ => { nextCalls++; return Task.CompletedTask; }, tracker);

        await middleware.InvokeAsync(CreateContext("/health", "UptimeRobot/2.0"));
        tracker.IsIdle().Should().BeTrue("an uptime ping must not wake the workers");

        await middleware.InvokeAsync(CreateContext("/posts/abc-slug", "Twitterbot/1.0"));
        tracker.IsIdle().Should().BeTrue("a crawler fetching the OG shell must not wake the workers");

        await middleware.InvokeAsync(CreateContext("/api/v1/posts", BrowserUserAgent));
        tracker.IsIdle().Should().BeFalse("a real request ends idleness immediately");

        nextCalls.Should().Be(3, "the middleware never short-circuits the pipeline");
    }

    private static HttpContext CreateContext(string path, string? userAgent)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (userAgent is not null)
            context.Request.Headers.UserAgent = userAgent;
        return context;
    }
}
