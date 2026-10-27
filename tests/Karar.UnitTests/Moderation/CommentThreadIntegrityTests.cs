using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

/// <summary>
/// Regression: cross-post reply injection. POST /api/v1/posts/X/comments accepted a parentId that
/// belonged to a comment on another post Y. The descendants CTE of GET /api/v1/posts/Y/comments then
/// rendered the reply inside Y's thread, with is_post_owner computed against X (the attacker's own post),
/// i.e. a forged "Yargılanan" (OP) badge. The reply notification also deep-linked to X.
/// The logic lives in raw SQL (Postgres is not available to unit tests), so these are source contracts.
/// </summary>
public sealed class CommentThreadIntegrityTests
{
    private static string ProgramText => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void CreateComment_RejectsParentCommentFromAnotherPost_BeforeInsert()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\", async (",
            "app.MapDelete(\"/api/v1/comments/{id:guid}\", async (");

        // Single implementation (shared with Security/CommentThreadIntegrityTests): the parent is looked up
        // bound to the route post id and must be active, in one query.
        const string parentLookup =
            "SELECT EXISTS(SELECT 1 FROM comments WHERE id = @parentId AND post_id = @postId AND status = 'active')";
        block.Should().Contain(parentLookup,
            "the parent comment must be loaded, required active and bound to the post in the route before a reply is attached to it");
        block.Should().Contain("parentCheck.Parameters.AddWithValue(\"postId\", id);",
            "the parent lookup must be bound to the post id of the route, not to a client-supplied value");
        CountOccurrences(block, "FROM comments WHERE id = @parentId").Should().Be(1,
            "exactly one parent-binding check must exist (no duplicate sequential lookups)");
        block.Should().Contain("BadRequest(\"INVALID_PARENT\"",
            "a foreign or inactive parent must be rejected with a 400");

        var check = block.IndexOf(parentLookup, StringComparison.Ordinal);
        var reject = block.IndexOf("BadRequest(\"INVALID_PARENT\"", StringComparison.Ordinal);
        var insert = block.IndexOf("INSERT INTO comments", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0);
        reject.Should().BeGreaterThan(check);
        reject.Should().BeLessThan(insert, "the parent check must run before the comment row is inserted");
    }

    [Fact]
    public void CommentList_DescendantsAreScopedToTheRoutePost()
    {
        var block = SliceBlock(ProgramText, "WITH RECURSIVE descendants AS (", "SELECT * FROM descendants");
        var members = block.Split("UNION ALL");

        members.Should().HaveCount(2, "the descendants CTE has an anchor and a recursive member");
        members[0].Should().Contain("cm.post_id = @postId",
            "replies stored under another post must not be rendered in this thread (anchor member)");
        members[1].Should().Contain("cm.post_id = @postId",
            "replies stored under another post must not be rendered in this thread (recursive member)");
    }

    [Fact]
    public void ReplyNotification_OnlyNotifiesParentAuthorOnTheSamePost()
    {
        var batcher = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "CommentNotificationBatcher.cs");
        var block = SliceBlock(
            batcher,
            "private async Task NotifyReplyAuthorAsync(",
            "protected override async Task ExecuteAsync(");

        block.Should().Contain("WHERE id = @parentId AND post_id = @postId AND status = 'active'",
            "a reply must not notify (and deep-link) the author of a comment on a different post");
        block.Should().Contain("command.Parameters.AddWithValue(\"postId\", postId);");
    }

    [Fact]
    public void Migration_EnforcesSamePostParentInTheDatabase()
    {
        // Single DB-level enforcement: the composite FK added by V100 (a second, trigger-based copy
        // of the same rule was collapsed when sec/01 and sec/05 were merged).
        var constraint = TestRepoPaths.ReadText("backend", "migrations", "V100__comment_parent_same_post.sql");
        constraint.Should().Contain("FOREIGN KEY (post_id, parent_id) REFERENCES comments (post_id, id)",
            "the database must require the parent comment to be on the same post");

        // V108 neutralizes replies injected before the constraint existed (V100 is NOT VALID, so it
        // does not touch legacy rows).
        var cleanup = TestRepoPaths.ReadText("backend", "migrations", "V108__comment_cross_post_reply_cleanup.sql");
        cleanup.Should().Contain("c.post_id <> pc.post_id",
            "already injected cross-post replies must be neutralized");
        cleanup.Should().Contain("SET status = 'deleted'");
        cleanup.Should().NotContain("CREATE TRIGGER",
            "the same-post rule is enforced once, by the V100 foreign key");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
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
