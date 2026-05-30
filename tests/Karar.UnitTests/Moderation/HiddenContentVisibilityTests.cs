using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

/// <summary>
/// Ensures that removed/hidden/under_review content never surfaces on public-facing endpoints.
/// Tests are string-block assertions on Program.cs — no DB integration needed.
/// </summary>
public sealed class HiddenContentVisibilityTests
{
    private static string ProgramText => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    // ── Main Feed ──────────────────────────────────────────────────────────

    [Fact]
    public void MainFeedEndpoint_ExcludesNonActiveAndUnlistedContent()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/posts\", async (",
            "app.MapGet(\"/api/v1/posts/discover\", async ("
        );

        block.Should().Contain("p.status = 'active'",
            "main feed must filter out deleted/hidden/under_review posts");
        block.Should().Contain("p.is_unlisted = FALSE",
            "main feed must not surface unlisted posts");
    }

    // ── Search ─────────────────────────────────────────────────────────────

    [Fact]
    public void SearchEndpoint_ExcludesNonActiveAndUnlistedContent()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/search\", async (",
            "app.MapGet(\"/api/v1/search/users\", async ("
        );

        block.Should().Contain("p.status = 'active'",
            "search must not return deleted/hidden/under_review posts");
        block.Should().Contain("p.is_unlisted = FALSE",
            "search must not return unlisted posts");
    }

    // ── Discover (section feed) ────────────────────────────────────────────

    [Fact]
    public void DiscoverEndpoint_AllSectionsExcludeNonActiveContent()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/discover\", async (",
            "app.MapGet(\"/api/v1/posts/discover/feed\", async ("
        );

        // rising, controversial, fresh and city-trending sub-queries must all carry the filter
        var statusOccurrences = CountOccurrences(block, "p.status = 'active'");
        statusOccurrences.Should().BeGreaterThanOrEqualTo(4,
            "rising, controversial, fresh and city-trending queries must each filter by active status");

        var unlistedOccurrences = CountOccurrences(block, "p.is_unlisted = FALSE");
        unlistedOccurrences.Should().BeGreaterThanOrEqualTo(4,
            "all discover sub-queries must exclude unlisted posts");
    }

    // ── Discover Feed (cursor-based immersive feed) ────────────────────────

    [Fact]
    public void DiscoverFeedEndpoint_ExcludesNonActiveAndUnlisted()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/discover/feed\", async (",
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\", async ("
        );

        block.Should().Contain("p.status = 'active'",
            "discover feed must exclude removed/hidden posts");
        block.Should().Contain("p.is_unlisted = FALSE",
            "discover feed must exclude unlisted posts");
    }

    // ── Similar Posts ──────────────────────────────────────────────────────

    [Fact]
    public void SimilarPostsEndpoint_ExcludesNonActiveAndUnlistedContent()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/{id:guid}/similar\", async (",
            "app.MapGet(\"/api/v1/trends/topics\", async ("
        );

        block.Should().Contain("p.status = 'active'",
            "similar posts must not include deleted/hidden posts");
        block.Should().Contain("p.is_unlisted = FALSE",
            "similar posts must not include unlisted posts");
    }

    // ── Public User Profile Posts ──────────────────────────────────────────

    [Fact]
    public void PublicUserPostsEndpoint_ExcludesNonActiveContent()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/posts\", async (",
            "app.MapGet(\"/api/v1/users/me/comments\", async ("
        );

        block.Should().Contain("status = 'active'",
            "public profile must not expose under_review, auto_hidden or deleted posts");
    }

    // ── Post Detail (deep-link / notification target) ──────────────────────

    [Fact]
    public void PostDetailEndpoint_Returns404ForNonActivePosts()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/{id:guid}\", async (",
            "app.MapPost(\"/api/v1/posts/{id:guid}/ai-summary\", async ("
        );

        block.Should().Contain("p.status = 'active'",
            "detail endpoint must gate on active status so removed posts are not publicly readable");
        block.Should().Contain("POST_NOT_FOUND",
            "non-active post must return 404 rather than leaking moderation state");
    }

    // ── Fresh epsilon-greedy slot ──────────────────────────────────────────

    [Fact]
    public void FreshSlotQuery_ExcludesNonActiveAndUnlistedContent()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "await using var freshCmd = new NpgsqlCommand(",
            "var freshPosts = LabelPosts(await ReadPostsAsync(freshCmd)"
        );

        // removed / hidden / under_review all fail the `status = 'active'` gate
        block.Should().Contain("p.status = 'active'",
            "fresh slot must not surface removed, hidden, or under_review posts");
        block.Should().Contain("p.is_unlisted = FALSE",
            "fresh slot must not surface unlisted posts");
    }

    // ── Owner's Own Posts ──────────────────────────────────────────────────

    [Fact]
    public void MyPostsEndpoint_ShowsOwnedPostsExceptDeleted()
    {
        var block = SliceEndpointBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/me/posts\", async (",
            "app.MapGet(\"/api/v1/users/me/saved\", async ("
        );

        // Owner may see under_review / auto_hidden posts — but never deleted
        block.Should().Contain("status != 'deleted'",
            "owner feed must exclude deleted posts");
        block.Should().NotContain("status = 'active'",
            "owner feed must not filter to active-only; owner should see their under_review and auto_hidden posts");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker '{startMarker}' must exist in Program.cs");
        end.Should().BeGreaterThan(start, $"end marker '{endMarker}' must appear after '{startMarker}'");

        return text[start..end];
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
