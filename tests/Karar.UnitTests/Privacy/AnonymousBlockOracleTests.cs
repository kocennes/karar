using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

// Blocking is keyed on user ids. If a block filter also hides anonymous content, it becomes an
// oracle: block user T, then see which anonymous posts/OP replies disappear (or which return
// BLOCKED_BY_AUTHOR) and they are attributed to T. Anonymous posts, and the anonymous author's
// replies on their own post, must therefore be treated as authorless by every block check.
// The blocker stays protected by the viewer-side comment filter and notification suppression.
public sealed class AnonymousBlockOracleTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void PostAuthorBlockFilters_DoNotHideAnonymousPosts()
    {
        var subqueries = BlockSubqueries("bu.blocked_user_id = p.user_id");

        subqueries.Should().HaveCountGreaterThanOrEqualTo(16,
            "feeds, discover, detail, ai-summary, search and notifications all filter by post author");
        foreach (var subquery in subqueries)
        {
            subquery.Should().Contain("AND p.is_anonymous = FALSE",
                "a block on the author must not hide an anonymous post (block-and-diff oracle)");
        }
    }

    [Fact]
    public void CommentAuthorBlockFilters_DoNotHideAnonymousOwnerReplies()
    {
        var subqueries = BlockSubqueries("bu.blocked_user_id = cm.user_id");

        subqueries.Should().HaveCountGreaterThanOrEqualTo(4,
            "comment count, top-level list and both descendant branches filter by comment author");
        foreach (var subquery in subqueries)
        {
            subquery.Should().Contain("AND NOT (p.is_anonymous AND p.user_id IS NOT DISTINCT FROM cm.user_id",
                "the anonymous OP's replies on their own post must not disappear when the OP is blocked");
        }
    }

    [Fact]
    public void CommentCount_JoinsPostSoAnonymousOwnerRepliesAreCountedConsistently()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/{id:guid}/comments\"",
            "const string commentSelectColumns");

        block.Should().Contain("JOIN posts p ON p.id = cm.post_id",
            "the pagination total must apply the same anonymous exemption as the list");
    }

    [Fact]
    public void NotificationCommentBlockFilters_DoNotHideAnonymousOwnerReplies()
    {
        var subqueries = BlockSubqueries("bu.blocked_user_id = nc.user_id");

        subqueries.Should().HaveCountGreaterThanOrEqualTo(3);
        foreach (var subquery in subqueries)
        {
            subquery.Should().Contain("AND NOT (p.is_anonymous AND p.user_id IS NOT DISTINCT FROM nc.user_id");
        }
    }

    [Fact]
    public void BlockedByAuthorCheck_DoesNotRevealAnonymousAuthors()
    {
        var helper = Slice(
            ProgramText,
            "static async Task<bool> IsBlockedByPostAuthorAsync",
            "static async Task<(CreatePostRequest? Request");

        helper.Should().Contain("p.is_anonymous = FALSE",
            "BLOCKED_BY_AUTHOR on an anonymous post tells the caller that someone who blocked them wrote it");
    }

    [Fact]
    public void ReplyNotificationBlockSuppression_ExemptsAnonymousPostAuthor()
    {
        // Once the anonymous OP's reply is visible in the thread, a missing reply notification
        // (suppressed because the parent author blocked the replier) would identify the OP.
        var batcher = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "CommentNotificationBatcher.cs");
        var replyBlock = Slice(batcher, "private async Task NotifyReplyAuthorAsync", "protected override async Task ExecuteAsync");

        replyBlock.Should().Contain("WHERE id = @postId AND is_anonymous = TRUE AND user_id = @commenterUserId");
        replyBlock.Should().Contain("blockCmd.Parameters.AddWithValue(\"postId\", postId)");
    }

    // Returns, for each occurrence of the author predicate, the text up to the first closing
    // parenthesis. The predicate is the last condition of its NOT EXISTS subquery, so an
    // exemption found here is guaranteed to be inside that subquery (not an outer filter).
    private static List<string> BlockSubqueries(string predicate)
    {
        var result = new List<string>();
        for (var idx = ProgramText.IndexOf(predicate, StringComparison.Ordinal);
             idx >= 0;
             idx = ProgramText.IndexOf(predicate, idx + predicate.Length, StringComparison.Ordinal))
        {
            var end = ProgramText.IndexOf(')', idx);
            result.Add(ProgramText[idx..end]);
        }

        return result;
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
