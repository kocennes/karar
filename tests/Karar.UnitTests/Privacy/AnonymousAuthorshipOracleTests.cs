using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

// Regression: blocking and author-diversity logic keyed on posts.user_id must not
// treat anonymous posts differently depending on who wrote them. Otherwise
// "block U, then GET /posts/X" (404 = U wrote X), BLOCKED_BY_AUTHOR on vote/comment,
// or per-author feed caps turn into authorship oracles for anonymous posts.
// The SQL lives inline in Program.cs and cannot run without Postgres, so these
// are source-contract tests.
public sealed class AnonymousAuthorshipOracleTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    // ── A) Block filter 404 / hidden-from-feed oracle ─────────────────────────

    [Fact]
    public void PostDetail_BlockFilter_DoesNotHideAnonymousPosts()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/{id:guid}\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/ai-summary\"");

        block.Should().MatchRegex(@"bu\.blocked_user_id = p\.user_id\s+AND p\.is_anonymous = FALSE",
            "a 404 after blocking U must not reveal that U wrote an anonymous post");
    }

    [Fact]
    public void EveryPostLevelBlockFilter_ExemptsAnonymousPosts()
    {
        // tail = rest of the NOT EXISTS subquery (up to its closing parenthesis), so the exemption
        // may sit on the same or the next line but must be inside that subquery.
        var matches = Regex.Matches(ProgramText, @"bu\.blocked_user_id = p\.user_id(?<tail>[^)]*)");

        matches.Count.Should().BeGreaterThanOrEqualTo(19,
            "feed, discover, detail, ai-summary, search and notification queries all filter by blocked authors");
        foreach (Match match in matches)
        {
            match.Groups["tail"].Value.TrimStart().Should().StartWith("AND p.is_anonymous = FALSE",
                "feed/search/notification visibility must not depend on the hidden author of an anonymous post");
        }
    }

    [Fact]
    public void CommentBlockFilters_DoNotHideOpRepliesOnAnonymousPosts()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"");

        // The blocked-commenter predicate lives in one shared fragment (sec/07) that is interpolated
        // into the count, top-level and both recursive descendant queries.
        Regex.Matches(block, Regex.Escape("{blockedCommentAuthorFilter}")).Count.Should().Be(4,
            "count, top-level and both recursive descendant queries filter blocked commenters");
        var matches = Regex.Matches(block, @"bu\.blocked_user_id = cm\.user_id(?<tail>[^\r\n]*)");
        matches.Count.Should().Be(1, "the predicate is defined once and reused");
        foreach (Match match in matches)
        {
            match.Groups["tail"].Value.Should().Contain(
                "NOT (p.is_anonymous AND p.user_id IS NOT DISTINCT FROM cm.user_id)",
                "the anonymous OP's own replies (isPostOwner=true) must not vanish when the viewer blocks the real author");
        }

        var countQuery = Slice(block, "await using var countCommand = new NpgsqlCommand(", "countCommand.Parameters");
        countQuery.Should().Contain("JOIN posts p ON p.id = cm.post_id",
            "the comment total must use the same anonymity-aware block filter as the list");
    }

    [Fact]
    public void NotificationCommentBlockFilters_DoNotHideAnonymousOpReplies()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/notifications\"",
            "app.MapPut(\"/api/v1/notifications/read-all\"");

        var matches = Regex.Matches(block, @"bu\.blocked_user_id = nc\.user_id(?<tail>[^\r\n]*)");
        matches.Count.Should().Be(3);
        foreach (Match match in matches)
        {
            match.Groups["tail"].Value.Should().Contain(
                "NOT (p.is_anonymous AND p.user_id IS NOT DISTINCT FROM nc.user_id)",
                "a reply notification from the anonymous OP must not disappear after blocking the real author");
        }
    }

    // ── B) BLOCKED_BY_AUTHOR oracle ───────────────────────────────────────────

    [Fact]
    public void BlockedByAuthorCheck_IsNotAppliedToAnonymousPosts()
    {
        var helper = Slice(
            ProgramText,
            "static async Task<bool> IsBlockedByPostAuthorAsync",
            "static async Task<(CreatePostRequest? Request");

        helper.Should().Contain("p.is_anonymous = FALSE",
            "403 BLOCKED_BY_AUTHOR on an anonymous post would reveal that its author blocked the caller");
    }

    // ── C) Per-author feed caps / consecutive-author skip ─────────────────────

    [Fact]
    public void AuthorDiversityWindows_PartitionAnonymousPostsSeparately()
    {
        var matches = Regex.Matches(
            ProgramText,
            @"PARTITION BY COALESCE\(p\.user_id::text, p\.device_id::text\)(?<tail>[^\r\n]*)");

        matches.Count.Should().Be(3, "main feed count, main feed list and discover feed cap posts per author");
        foreach (Match match in matches)
        {
            match.Groups["tail"].Value.Should().StartWith(", CASE WHEN p.is_anonymous THEN p.id END",
                "an anonymous post must not share the author_rank bucket of its author's public posts");
        }
    }

    [Fact]
    public void RankingAuthorKey_IsPerPostForAnonymousPosts()
    {
        var keys = Regex.Matches(ProgramText, @"(?<expr>[^\r\n]*) AS ranking_author_key");

        keys.Count.Should().Be(3);
        foreach (Match match in keys)
        {
            match.Groups["expr"].Value.Trim().Should().EndWith(
                "CASE WHEN p.is_anonymous THEN p.id ELSE COALESCE(p.user_id, p.device_id) END",
                "the no-consecutive-author pass must not skip an anonymous post because of its hidden author");
        }
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"marker '{start}' must exist");
        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"marker '{end}' must follow '{start}'");
        return text[startIndex..endIndex];
    }
}
