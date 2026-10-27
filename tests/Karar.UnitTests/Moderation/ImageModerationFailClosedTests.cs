using System.Net;
using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Karar.UnitTests.Moderation;

/// <summary>
/// Regression: image moderation failed open. Feed queries hid images whose SafeSearch verdict was
/// still pending, but post detail, search, today, city trending, similar, weekly featured, profiles,
/// saved posts and the crawler OG/link-preview pages served the raw image_url while pending. The worker
/// also approved images "by default" whenever Vision returned no verdict, and only looked at the newest
/// 20 pending rows from the last 6 hours.
/// </summary>
public sealed class ImageModerationFailClosedTests
{
    private const string ApprovedImageGuard =
        "CASE WHEN p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved' THEN p.image_url END AS image_url";

    private static string ProgramText => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string WorkerText =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "ImageModerationWorker.cs");

    [Fact]
    public void EveryPublicQueryThatSelectsImageUrl_AppliesTheImageModerationRule()
    {
        var text = ProgramText;
        var offenders = new List<string>();

        // Raw SQL lives in """ ... """ literals; odd segments are literal bodies.
        var segments = text.Split("\"\"\"");
        var offset = 0;
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (i % 2 == 1
                && segment.Contains("image_url", StringComparison.Ordinal)
                && !segment.Contains("INSERT INTO posts", StringComparison.Ordinal))
            {
                var route = RouteAt(text, offset);
                if (!route.StartsWith("/api/v1/admin/", StringComparison.Ordinal)
                    && !segment.Contains("image_moderation_status", StringComparison.Ordinal))
                {
                    offenders.Add(route);
                }
            }

            offset += segment.Length + 3;
        }

        offenders.Should().BeEmpty(
            "a public endpoint must never return image_url while SafeSearch has not approved the image (offending routes: {0})",
            string.Join(", ", offenders));
    }

    [Theory]
    [InlineData("app.MapGet(\"/api/v1/posts/{id:guid}\", async (", "app.MapPost(\"/api/v1/posts/{id:guid}/ai-summary\", async (")]
    [InlineData("app.MapGet(\"/api/v1/posts/today\", async (", "app.MapGet(\"/api/v1/posts/trending/city\", async (")]
    [InlineData("app.MapGet(\"/api/v1/search\", async (", "app.MapGet(\"/api/v1/search/users\", async (")]
    [InlineData("app.MapGet(\"/api/v1/users/{username}/posts\", async (", "app.MapGet(\"/api/v1/users/me/comments\", async (")]
    [InlineData("app.MapGet(\"/api/v1/users/me/posts\", async (", "app.MapPut(\"/api/v1/users/me/password\", async (")]
    public void DetailSearchAndProfileQueries_ReturnImageOnlyWhenApproved(string startMarker, string endMarker)
    {
        var block = SliceBlock(ProgramText, startMarker, endMarker);

        block.Should().Contain(ApprovedImageGuard,
            "the post stays visible but its image is withheld until SafeSearch approves it");
    }

    [Theory]
    [InlineData("app.MapGet(\"/posts/{id:guid}\"", "app.MapGet(\"/posts/{slug}\"")]
    [InlineData("app.MapGet(\"/posts/{slug}\"", "app.MapGet(\"/api/v1/posts/{id:guid}/share-image.png\"")]
    public void CrawlerOgPages_NeverUseUnmoderatedImagesForLinkPreviews(string startMarker, string endMarker)
    {
        var block = SliceBlock(ProgramText, startMarker, endMarker);

        block.Should().Contain(
            "CASE WHEN image_moderation_status IS NULL OR image_moderation_status = 'approved' THEN image_url END",
            "og:image previews are cached by WhatsApp/Telegram/X and must fall back to the share card");
    }

    [Fact]
    public void Worker_DoesNotApproveImagesWhenSafeSearchGivesNoVerdict()
    {
        var worker = WorkerText;

        worker.Should().NotContain("approved by default",
            "a Vision outage, quota error or missing annotation must never approve an image");
        worker.Should().Contain("Decide(result, attempts)",
            "the per-image outcome must go through the fail-closed decision");
        worker.Should().Contain("DeferAsync(",
            "an image without a verdict must stay pending and be retried later");
    }

    [Fact]
    public void Worker_ProcessesOldestDueImagesWithoutDroppingOldOnes()
    {
        var worker = WorkerText;

        worker.Should().Contain("ORDER BY created_at ASC",
            "a burst of new uploads must not starve older pending images");
        worker.Should().NotContain("INTERVAL '6 hours'",
            "pending images older than 6 hours must not be abandoned");
        worker.Should().Contain("image_moderation_next_attempt_at <= NOW()",
            "failing images back off so they cannot block the head of the queue");
    }

    [Fact]
    public void CreatePost_RejectsImagesWhenImageModerationIsNotConfigured()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/posts\", async (",
            "app.MapGet(\"/api/v1/posts/{id:guid}\", async (");

        block.Should().Contain("!safeSearch.IsEnabled",
            "without Vision:ApiKey no image can ever be moderated");
        block.Should().Contain("IMAGE_MODERATION_UNAVAILABLE");
        block.IndexOf("IMAGE_MODERATION_UNAVAILABLE", StringComparison.Ordinal)
            .Should().BeLessThan(block.IndexOf("storageService.UploadAsync(", StringComparison.Ordinal),
                "nothing may be uploaded to the public bucket before moderation is known to be available");
    }

    // ── Behaviour: the per-image decision ─────────────────────────────────

    [Fact]
    public void Decide_WithoutVerdict_KeepsImageHiddenAndRetries()
    {
        var decision = ImageModerationWorker.Decide(result: null, previousAttempts: 0);

        decision.IsFinal.Should().BeFalse();
        decision.ModerationStatus.Should().BeNull("no verdict must never turn into 'approved'");
        decision.PostStatus.Should().BeNull();
        decision.RetryAfter.Should().Be(TimeSpan.FromMinutes(1));
        decision.RaiseAlert.Should().BeFalse();
    }

    [Fact]
    public void Decide_RepeatedFailures_BackOffExponentiallyAndAlertOnce()
    {
        ImageModerationWorker.Decide(null, 1).RetryAfter.Should().Be(TimeSpan.FromMinutes(2));
        ImageModerationWorker.Decide(null, 3).RetryAfter.Should().Be(TimeSpan.FromMinutes(8));
        ImageModerationWorker.Decide(null, 500).RetryAfter.Should().Be(ImageModerationWorker.MaxRetryDelay);

        ImageModerationWorker.Decide(null, ImageModerationWorker.AlertAfterAttempts - 1).RaiseAlert.Should().BeTrue();
        ImageModerationWorker.Decide(null, ImageModerationWorker.AlertAfterAttempts).RaiseAlert.Should().BeFalse(
            "the stalled-moderation alert is raised once, not on every retry");
    }

    [Theory]
    [InlineData("VERY_UNLIKELY", "VERY_UNLIKELY", "UNLIKELY", "active", "approved")]
    [InlineData("LIKELY", "VERY_UNLIKELY", "VERY_UNLIKELY", "auto_hidden", "rejected")]
    [InlineData("VERY_UNLIKELY", "VERY_UNLIKELY", "VERY_LIKELY", "under_review", "rejected")]
    public void Decide_WithVerdict_IsFinal(string adult, string violence, string racy, string postStatus, string moderationStatus)
    {
        var decision = ImageModerationWorker.Decide(new SafeSearchResult(adult, violence, racy, "UNKNOWN"), previousAttempts: 3);

        decision.IsFinal.Should().BeTrue();
        decision.PostStatus.Should().Be(postStatus);
        decision.ModerationStatus.Should().Be(moderationStatus);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"message":"quota"}}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"billing disabled"}}""")]
    [InlineData(HttpStatusCode.OK, """{"responses":[{"error":{"code":7,"message":"image could not be fetched"}}]}""")]
    public async Task VisionErrors_YieldNoVerdict_AndTheImageIsNotApproved(HttpStatusCode status, string body)
    {
        var http = new StubHttpClientFactory(_ => StubHttpClientFactory.Json(status, body));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Vision:ApiKey"] = "unit-test-placeholder" })
            .Build();
        var safeSearch = new SafeSearchService(http, config, NullLogger<SafeSearchService>.Instance);

        var result = await safeSearch.AnalyzeAsync("https://storage.example.test/posts/a.jpg");
        var decision = ImageModerationWorker.Decide(result, previousAttempts: 0);

        result.Should().BeNull();
        http.Requests.Should().HaveCount(1);
        decision.ModerationStatus.Should().NotBe("approved",
            "quota, key/billing and per-image errors used to approve unmoderated images by default");
        decision.IsFinal.Should().BeFalse();
    }

    [Fact]
    public void Migration_AddsRetryStateForImageModeration()
    {
        var sql = TestRepoPaths.ReadText("backend", "migrations", "V109__image_moderation_retry_state.sql");

        sql.Should().Contain("image_moderation_attempts");
        sql.Should().Contain("image_moderation_next_attempt_at");
    }

    private static string RouteAt(string text, int index)
    {
        var mapIndex = text.LastIndexOf("app.Map", index, StringComparison.Ordinal);
        if (mapIndex < 0) return string.Empty;
        var open = text.IndexOf("(\"", mapIndex, StringComparison.Ordinal);
        var close = text.IndexOf('"', open + 2);
        return open < 0 || close < 0 ? string.Empty : text[(open + 2)..close];
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
