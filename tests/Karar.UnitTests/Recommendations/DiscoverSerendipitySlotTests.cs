using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

public sealed class DiscoverSerendipitySlotTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string DiscoverFeedBlock => SliceBlock(
        ProgramText,
        "app.MapGet(\"/api/v1/posts/discover/feed\"",
        "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

    private static string SerendipityBlock => SliceBlock(
        DiscoverFeedBlock,
        "Serendipity: every 10 posts",
        "Append UCB Stage 1 exploration posts");

    [Fact]
    public void DiscoverFeed_Serendipity_UsesUserOrDeviceSevenDayAffinity()
    {
        SerendipityBlock.Should().Contain("user_category_affinity uca");
        SerendipityBlock.Should().Contain("device_category_affinity dca");
        SerendipityBlock.Should().Contain("INTERVAL '7 days'",
            because: "serendipity must target categories without recent interaction");
    }

    [Fact]
    public void DiscoverFeed_Serendipity_CarriesRankingReasonAndOneToTwoSlots()
    {
        SerendipityBlock.Should().Contain("Math.Clamp(limit / 10, 1, 2)",
            because: "discover serendipity should inject 1-2 posts per every 10 results");
        SerendipityBlock.Should().Contain("RankingReason = \"serendipity\"",
            because: "clients need the serendipity ranking reason for badges and analytics");
        SerendipityBlock.Should().Contain("10 * (i + 1) - 1",
            because: "serendipity should be placed around every tenth card");
        SerendipityBlock.Should().Contain("result[replaceAt] = serendipityPost",
            because: "serendipity should replace a main candidate so the response does not exceed limit");
    }

    [Fact]
    public void DiscoverFeed_Serendipity_ReusesSafetyAndPersonalFilters()
    {
        SerendipityBlock.Should().Contain("p.status = 'active'");
        SerendipityBlock.Should().Contain("p.is_unlisted = FALSE");
        SerendipityBlock.Should().Contain("p.distribution_stage >= 2");
        SerendipityBlock.Should().Contain("p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6");
        SerendipityBlock.Should().Contain("p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved'");
        SerendipityBlock.Should().Contain("blocked_users bu");
        SerendipityBlock.Should().Contain("muted_categories mc");
        SerendipityBlock.Should().Contain("suppressedPostsWhere");
        SerendipityBlock.Should().Contain("suppressedCategoriesWhere");
    }

    [Fact]
    public void DiscoverFeed_Serendipity_AvoidsDuplicatesAndSeenPosts()
    {
        SerendipityBlock.Should().Contain("p.id != ALL(@usedIds)");
        SerendipityBlock.Should().Contain("post_views pv");
        SerendipityBlock.Should().Contain("usedIds.Add(serendipityPost.Id)");
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
