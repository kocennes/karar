using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

public sealed class DiscoverAuthorDiversityTests
{
    [Fact]
    public void DiscoverFeed_ConsecutiveAuthorGuard_UsesInternalRankingAuthorKey()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var discoverFeedBlock = SliceBlock(
            programText,
            "app.MapGet(\"/api/v1/posts/discover/feed\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

        discoverFeedBlock.Should().Contain("COALESCE(p.user_id, p.device_id) AS ranking_author_key",
            because: "the in-memory no-consecutive-author pass needs a stable author/device key");
        discoverFeedBlock.Should().Contain("prevRankingAuthorKey",
            because: "discover/feed must compare consecutive posts by ranking_author_key, not public author_id");
        discoverFeedBlock.Should().Contain("post.RankingAuthorKey",
            because: "the diversity pass must use the internal key even for anonymous posts");
    }

    [Fact]
    public void PostDto_RankingAuthorKey_IsNotSerialized()
    {
        var domainText = TestRepoPaths.ReadText("backend", "Karar.Api", "Models", "Domain.cs");

        domainText.Should().Contain("Guid? RankingAuthorKey");
        domainText.Should().Contain("JsonIgnore",
            because: "ranking_author_key is an internal diversity signal and must not leak in API JSON");
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }
}
