using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

public sealed class AnonymousPostContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void MainFeedDiversityPass_SelectsAnonymityWithoutConfusingOwnership()
    {
        var block = SliceBlock(
            ProgramText,
            "WITH base AS (",
            "var freshSlotTarget = applyDiversityPass");

        block.Should().NotContain("p.is_anonymous AS is_owner",
            "anonymity and ownership are separate concepts and must not share the same alias");
        block.Should().Contain("vote_type, trend_score, created_at, is_owner, is_anonymous",
            "page-1 diversity feed rows must still expose is_anonymous so ReadPostsAsync redacts authors");
    }

    [Fact]
    public void PublicUserPosts_RedactsAnonymousAuthorAndSelectsAnonymityFlag()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/posts\"",
            "app.MapGet(\"/api/v1/users/{username}/comments\"");

        block.Should().Contain("CASE WHEN p.is_anonymous THEN NULL ELSE u.username END",
            "public profile post cards must not reveal the real author name for anonymous posts");
        block.Should().Contain("p.is_anonymous",
            "clients need the anonymity flag to render the anonymous author affordance");
    }

    [Fact]
    public void PublicUserPosts_ExcludesAnonymousAndUnlistedPostsFromCountAndList()
    {
        // Listing anonymous posts under the author's username de-anonymizes them, even with the
        // card name redacted; unlisted (link-only) posts must not be discoverable from a profile.
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/posts\"",
            "app.MapGet(\"/api/v1/users/me/comments\"");
        var countSql = SliceBlock(block, "countCmd = new NpgsqlCommand(", "countCmd.Parameters");
        var listSql = SliceBlock(block, "await using var cmd = new NpgsqlCommand(", "cmd.Parameters");

        countSql.Should().Contain("is_anonymous = FALSE",
            "the public total must not count anonymous posts, otherwise it leaks how many the user wrote");
        countSql.Should().Contain("is_unlisted = FALSE");
        listSql.Should().Contain("p.is_anonymous = FALSE",
            "anonymous posts must never be listed under the author's public profile");
        listSql.Should().Contain("p.is_unlisted = FALSE",
            "link-only posts must not be listed on public profiles");
    }

    [Fact]
    public void PublicProfileAndUserSearch_CountsExcludeAnonymousActivity()
    {
        // Public counters must match what the public lists show; otherwise polling a user's
        // counter and correlating it with new anonymous posts/comments attributes them.
        var profile = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/profile\"",
            "app.MapPost(\"/api/v1/auth/2fa/setup\"");
        var profilePostCount = SliceBlock(profile, "(SELECT COUNT(*) FROM posts p", "AS post_count");
        profilePostCount.Should().Contain("p.status = 'active'");
        profilePostCount.Should().Contain("p.is_anonymous = FALSE");
        profilePostCount.Should().Contain("p.is_unlisted = FALSE");

        var profileCommentCount = SliceBlock(profile, "(SELECT COUNT(*) FROM comments cm", "AS comment_count");
        profileCommentCount.Should().Contain("NOT (cp.is_anonymous AND cp.user_id IS NOT DISTINCT FROM cm.user_id)",
            "replies by the anonymous author on their own post are shown as '@anonim' and must not move the public counter");
        profileCommentCount.Should().Contain("cp.is_unlisted = FALSE");

        var userSearch = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/search/users\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"");
        var searchPostCount = SliceBlock(userSearch, "(SELECT COUNT(*) FROM posts p", "AS post_count");
        searchPostCount.Should().Contain("p.is_anonymous = FALSE");
        searchPostCount.Should().Contain("p.is_unlisted = FALSE");
    }

    [Fact]
    public void PublicUserComments_ExcludeOwnAnonymousPostRepliesAndUnlistedPosts()
    {
        // A comment the author wrote on their own anonymous post is rendered as '@anonim [Yargilanan]';
        // listing it (with the post id/title) under their username reveals who wrote the post.
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/comments\"",
            "app.MapGet(\"/api/v1/users/me/karma-history\"");
        var countSql = SliceBlock(block, "countCmd = new NpgsqlCommand(", "countCmd.Parameters");
        var listSql = SliceBlock(block, "await using var cmd = new NpgsqlCommand(", "cmd.Parameters");

        foreach (var sql in new[] { countSql, listSql })
        {
            sql.Should().Contain("NOT (p.is_anonymous AND p.user_id IS NOT DISTINCT FROM cm.user_id)",
                "null-safe: a reply on a guest's (user_id NULL) anonymous post must not be dropped by a NULL comparison");
            sql.Should().Contain("p.is_unlisted = FALSE");
        }
    }

    [Fact]
    public void ReadCommentDtoAsync_RedactsAnonymousPostOwnerIdentity()
    {
        // Reaction add/remove return this DTO; it must redact the anonymous OP exactly like the list does.
        var block = SliceBlock(
            ProgramText,
            "static async Task<CommentDto?> ReadCommentDtoAsync",
            "static async Task<IResult> SetCommentUpvoteAsync");

        block.Should().Contain("CASE WHEN (p.is_anonymous = TRUE AND cm.user_id = p.user_id) OR u.deleted_at IS NOT NULL THEN NULL ELSE u.username END");
        block.Should().Contain("CASE WHEN (p.is_anonymous = TRUE AND cm.user_id = p.user_id) OR u.deleted_at IS NOT NULL THEN NULL ELSE cm.user_id END");
        block.Should().NotContain("u.username, cm.user_id,",
            "the raw author name/id of the anonymous post owner must not be projected");
    }

    [Fact]
    public void EveryCommentProjectionJoiningAuthors_RedactsAnonymousPostOwner()
    {
        const string authorJoin = "LEFT JOIN users u ON u.id = cm.user_id";
        var listColumns = SliceBlock(ProgramText, "const string commentSelectColumns", "static CommentDto ReadCommentRow");
        listColumns.Should().Contain("CASE WHEN (p.is_anonymous = TRUE AND cm.user_id = p.user_id) OR u.deleted_at IS NOT NULL THEN NULL ELSE u.username END");

        var found = 0;
        for (var idx = ProgramText.IndexOf(authorJoin, StringComparison.Ordinal);
             idx >= 0;
             idx = ProgramText.IndexOf(authorJoin, idx + authorJoin.Length, StringComparison.Ordinal))
        {
            found++;
            var sqlStart = ProgramText.LastIndexOf("\"\"\"", idx, StringComparison.Ordinal);
            var sqlEnd = ProgramText.IndexOf("\"\"\"", idx, StringComparison.Ordinal);
            var sql = ProgramText[sqlStart..sqlEnd];
            (sql.Contains("p.is_anonymous", StringComparison.Ordinal) ||
             sql.Contains("{commentSelectColumns}", StringComparison.Ordinal))
                .Should().BeTrue($"comment SQL joining author names must redact the anonymous post owner:{Environment.NewLine}{sql}");
        }

        found.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ReadPostsAsync_UsesColumnNamesForOptionalFields()
    {
        var block = SliceBlock(
            ProgramText,
            "static async Task<IReadOnlyList<PostDto>> ReadPostsAsync",
            "static async Task<(IReadOnlyList<PostDto> Posts, int Total)> ReadPostsWithTotalAsync");

        block.Should().Contain("ReadBool(\"is_unlisted\")",
            "optional fields appear in different query shapes and must not depend on fragile column counts");
        block.Should().Contain("ReadBool(\"is_anonymous\")",
            "anonymous redaction must work for every query that selects the flag");
        block.Should().Contain("ReadString(\"status\")",
            "my-posts queries include status where feed queries include is_unlisted");
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
