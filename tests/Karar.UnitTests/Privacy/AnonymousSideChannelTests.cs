using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

// Regression: public per-user aggregates (karma, post_count, comment_count, the public
// posts/comments lists) must not move because of anonymous posts or the OP's own
// comments on them. Otherwise "cast the 10th vote, watch whose karma goes +1" or
// "watch post_count while sort=new shows a new anonymous post" links the post to its author.
public sealed class AnonymousSideChannelTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    // ── Karma milestones ──────────────────────────────────────────────────────

    [Fact]
    public void PostVoteMilestone_DoesNotCreditPublicKarmaForAnonymousPosts()
    {
        var block = Slice(
            ProgramText,
            "static async Task CheckPostVoteMilestoneAsync",
            "static async Task CheckCommentUpvoteMilestoneAsync");

        block.Should().Contain("SELECT user_id, device_id, is_anonymous FROM posts WHERE id = @postId",
            "the milestone helper must know whether the post is anonymous");
        block.Should().Contain("if (userId.HasValue && !isAnonymousPost)",
            "a +1/+2/+3/+5/+10 karma jump at a vote milestone would identify the anonymous author");
    }

    [Fact]
    public void CommentUpvoteMilestone_DoesNotCreditKarmaForAnonymousOpComments()
    {
        var block = Slice(
            ProgramText,
            "static async Task CheckCommentUpvoteMilestoneAsync",
            "static async Task<int> UpdateModerationTargetAsync");

        block.Should().Contain("JOIN posts p ON p.id = cm.post_id",
            "the helper must see the parent post to detect the anonymous OP");
        block.Should().Contain("p.is_anonymous AND p.user_id = cm.user_id",
            "the OP's own comments on an anonymous post must not move public karma");
    }

    // ── Public counters ───────────────────────────────────────────────────────

    [Fact]
    public void PublicProfile_CountsExcludeAnonymousPostsAndOpComments()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/profile\"",
            "app.MapPost(\"/api/v1/auth/2fa/setup\"");

        var postCount = Slice(block, "(SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id", "AS post_count");
        postCount.Should().Contain("p.status = 'active'", "public post_count must not include hidden posts");
        postCount.Should().Contain("p.is_anonymous = FALSE",
            "public post_count must not include anonymous posts");

        var commentCount = Slice(block, "(SELECT COUNT(*) FROM comments cm", "AS comment_count");
        commentCount.Should().Contain("NOT (cp.is_anonymous AND cp.user_id IS NOT DISTINCT FROM cm.user_id)",
            "public comment_count must not include the OP's comments on their anonymous posts");
    }

    [Fact]
    public void UserSearch_PostCountExcludesAnonymousPosts()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/search/users\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"");

        var postCount = Slice(block, "(SELECT COUNT(*) FROM posts p WHERE p.user_id = u.id", "AS post_count");
        postCount.Should().Contain("p.status = 'active'");
        postCount.Should().Contain("p.is_anonymous = FALSE");
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
