using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

/// <summary>
/// Pins the diversity, fresh-slot, and serendipity rules that keep the main
/// feed and discover/feed varied — same-author cap, category cap, fresh 20%
/// epsilon-greedy, and 7-day serendipity window.
/// </summary>
public sealed class FeedDiversityRuleTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    // ── Slice helpers ────────────────────────────────────────────────────────

    // Main feed endpoint block (covers diversity pass SQL and freshSlotTarget)
    private static string MainFeedBlock => SliceBlock(
        ProgramText,
        "app.MapGet(\"/api/v1/posts\", async (",
        "app.MapGet(\"/api/v1/posts/discover\", async (");

    // Fresh epsilon-greedy query block
    private static string FreshCmdBlock => SliceBlock(
        ProgramText,
        "await using var freshCmd = new NpgsqlCommand(",
        "var freshPosts = LabelPosts(await ReadPostsAsync(freshCmd)");

    // Serendipity query block
    private static string SerendipityCmdBlock => SliceBlock(
        ProgramText,
        "await using var serendipityCmd = new NpgsqlCommand(",
        "var serendipityPosts = LabelPosts(await ReadPostsAsync(serendipityCmd)");

    // Discover/feed endpoint block
    private static string DiscoverFeedBlock => SliceBlock(
        ProgramText,
        "app.MapGet(\"/api/v1/posts/discover/feed\"",
        "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

    // ── Main feed — diversity pass ────────────────────────────────────────────

    [Fact]
    public void MainFeed_DiversityPass_CapsPerCategoryAt5()
    {
        MainFeedBlock.Should().Contain("cat_rank <= 5",
            because: "diversity pass must allow at most 5 posts per category on page 1");
    }

    [Fact]
    public void MainFeed_DiversityPass_CapsPerAuthorAt3()
    {
        MainFeedBlock.Should().Contain("author_rank <= 3",
            because: "diversity pass must allow at most 3 posts per author/device on page 1");
    }

    // ── Main feed — fresh epsilon-greedy slot ────────────────────────────────

    [Fact]
    public void MainFeed_FreshSlot_Targets20Percent()
    {
        MainFeedBlock.Should().Contain("0.20",
            because: "fresh slot target must be 20% of the page limit (epsilon-greedy exploration)");
    }

    [Fact]
    public void MainFeed_FreshSlot_Uses2HourWindow()
    {
        FreshCmdBlock.Should().Contain("'2 hours'",
            because: "fresh posts are limited to those created within the last 2 hours");
    }

    [Fact]
    public void MainFeed_FreshSlot_LabeledAsFresh()
    {
        MainFeedBlock.Should().Contain("LabelPosts(await ReadPostsAsync(freshCmd), \"fresh\"",
            because: "fresh slot posts must carry ranking_reason='fresh' so clients can render the correct badge");
    }

    // ── Main feed — serendipity ───────────────────────────────────────────────

    [Fact]
    public void MainFeed_Serendipity_Uses7DayInteractionWindow()
    {
        SerendipityCmdBlock.Should().Contain("'7 days'",
            because: "serendipity selects categories the user has not interacted with in the last 7 days");
    }

    [Fact]
    public void MainFeed_Serendipity_LabeledAsSerendipity()
    {
        MainFeedBlock.Should().Contain("LabelPosts(await ReadPostsAsync(serendipityCmd), \"serendipity\"",
            because: "serendipity posts must carry ranking_reason='serendipity'");
    }

    [Fact]
    public void MainFeed_Serendipity_InjectedEvery10Posts()
    {
        MainFeedBlock.Should().Contain("posts.Count / 10",
            because: "serendipity injects 1–2 posts per every 10 posts in the result");
    }

    // ── Discover feed — same-author cap ──────────────────────────────────────

    [Fact]
    public void DiscoverFeed_SameAuthorCap_At2()
    {
        DiscoverFeedBlock.Should().Contain("author_rank <= 2",
            because: "discover/feed must cap the same author/device to at most 2 posts per page via SQL window function");
    }

    // ── Discover feed — category diversity (in-memory pass) ──────────────────

    [Fact]
    public void DiscoverFeed_CategoryCap_At3()
    {
        DiscoverFeedBlock.Should().Contain("catCount >= 3",
            because: "discover/feed in-memory diversity pass must skip posts once a category has 3 results");
    }

    [Fact]
    public void DiscoverFeed_ConsecutiveCategoryStreak_At2()
    {
        DiscoverFeedBlock.Should().Contain("streak > 2",
            because: "discover/feed must skip a post when the same category appears more than 2 times in a row");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }
}
