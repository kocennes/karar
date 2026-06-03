using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

/// <summary>
/// Contract tests that pin the rankingReason / rankingLabel fields on
/// API response types and the endpoint code that populates them.
///
/// These tests break the build if a field is renamed, removed, or if the
/// set of valid reason strings drifts — protecting analytics pipelines and
/// client parsers that depend on a stable vocabulary.
/// </summary>
public sealed class RankingContractTests
{
    private static readonly string ResponsesText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Contracts", "Responses.cs");

    private static readonly string DomainText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Models", "Domain.cs");

    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    // Slice the main feed endpoint (GET /api/v1/posts)
    private static string FeedEndpointBlock => SliceBlock(
        ProgramText,
        "app.MapGet(\"/api/v1/posts\"",
        "app.MapGet(\"/api/v1/posts/discover\"");

    // Slice the discover/feed endpoint
    private static string DiscoverFeedBlock => SliceBlock(
        ProgramText,
        "app.MapGet(\"/api/v1/posts/discover/feed\"",
        "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

    // ── FeedResponse — envelope-level rankingLabel ──────────────────────────

    [Fact]
    public void FeedResponse_HasRankingLabelProperty()
    {
        ResponsesText.Should().Contain("RankingLabel",
            because: "FeedResponse must carry a rankingLabel so clients know which sort was applied");
    }

    [Fact]
    public void FeedResponse_RankingLabelIsNullable()
    {
        // The record parameter must be typed `string? RankingLabel`
        ResponsesText.Should().MatchRegex(@"string\?\s+RankingLabel",
            because: "rankingLabel is optional — omitted for endpoints that don't set it");
    }

    [Fact]
    public void FeedEndpoint_ComputesRankingLabelViaSwitch()
    {
        FeedEndpointBlock.Should().Contain("rankingLabel",
            because: "GET /api/v1/posts must compute and include rankingLabel in FeedResponse");
        FeedEndpointBlock.Should().Contain("switch",
            because: "rankingLabel is derived from sort+categoryId via a switch expression");
    }

    [Fact]
    public void FeedEndpoint_RankingLabelValues_AreInventoried()
    {
        // Pin all valid values so renames don't silently break client expectations
        FeedEndpointBlock.Should().Contain("\"trending\"",
            because: "default sort without category must yield label 'trending'");
        FeedEndpointBlock.Should().Contain("\"new\"",
            because: "sort=new without category must yield label 'new'");
        FeedEndpointBlock.Should().Contain("\"category_new\"",
            because: "sort=new with category must yield label 'category_new'");
        FeedEndpointBlock.Should().Contain("\"category_trending\"",
            because: "sort=trending with category must yield label 'category_trending'");
    }

    // ── DiscoverFeedItem — per-item rankingReason ────────────────────────────

    [Fact]
    public void DiscoverFeedItem_HasNonNullableRankingReason()
    {
        // `string RankingReason` — no `?` means the field is always present
        ResponsesText.Should().MatchRegex(@"string\s+RankingReason",
            because: "DiscoverFeedItem.RankingReason must be non-nullable; every discover item must carry a reason");
    }

    [Fact]
    public void DiscoverFeedEndpoint_UsesRankingReasonForPerPost()
    {
        DiscoverFeedBlock.Should().Contain("RankingReasonFor",
            because: "discover/feed must call RankingReasonFor to compute a label for every result post");
        DiscoverFeedBlock.Should().Contain("RankingReason ?? \"trending\"",
            because: "the fallback must be 'trending' when no reason is computed");
    }

    [Fact]
    public void DiscoverFeedEndpoint_RankingReasonValues_AreInventoried()
    {
        // All 4 reason values must appear in the RankingReasonFor function
        DiscoverFeedBlock.Should().Contain("\"rising\"",
            because: "age < 6h AND votes >= 15 yields 'rising'");
        DiscoverFeedBlock.Should().Contain("\"controversial\"",
            because: "votes > 40 AND balance < 20% yields 'controversial'");
        DiscoverFeedBlock.Should().Contain("\"fresh\"",
            because: "age < 2h AND votes <= 10 yields 'fresh'");
        DiscoverFeedBlock.Should().Contain("\"trending\"",
            because: "'trending' is the catch-all default reason");
    }

    [Fact]
    public void DiscoverFeedEndpoint_RankingReasonFor_UsesThresholdValues()
    {
        // Rising: age < 6 hours, >= 15 votes
        DiscoverFeedBlock.Should().Contain("FromHours(6)",
            because: "rising threshold is posts younger than 6 hours");
        DiscoverFeedBlock.Should().Contain("15",
            because: "rising requires at least 15 votes");
        // Controversial: > 40 votes, balance < 20%
        DiscoverFeedBlock.Should().Contain("40",
            because: "controversial requires > 40 total votes");
        DiscoverFeedBlock.Should().Contain("0.2",
            because: "controversial requires hakli/haksiz balance < 20%");
        // Fresh: age < 2 hours, <= 10 votes
        DiscoverFeedBlock.Should().Contain("FromHours(2)",
            because: "fresh threshold is posts younger than 2 hours per ranking-model.md §5 unified definition");
        DiscoverFeedBlock.Should().Contain("10",
            because: "fresh requires <= 10 votes");
    }

    // ── PostDto — per-post ranking fields with explicit JSON names ───────────

    [Fact]
    public void PostDto_RankingReason_HasSnakeCaseJsonPropertyName()
    {
        DomainText.Should().Contain("JsonPropertyName(\"ranking_reason\")",
            because: "clients parse 'ranking_reason' (snake_case) from PostDto items");
    }

    [Fact]
    public void PostDto_RankingLabel_HasSnakeCaseJsonPropertyName()
    {
        DomainText.Should().Contain("JsonPropertyName(\"ranking_label\")",
            because: "clients parse 'ranking_label' (snake_case) from PostDto items");
    }

    [Fact]
    public void PostDto_RankingFieldsAreNullable()
    {
        // Both are nullable — they are omitted from standard feed items and set only when relevant
        DomainText.Should().MatchRegex(@"string\?\s+RankingReason",
            because: "PostDto.RankingReason is nullable; absent on standard feed items");
        DomainText.Should().MatchRegex(@"string\?\s+RankingLabel",
            because: "PostDto.RankingLabel is nullable; absent on standard feed items");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }
}
