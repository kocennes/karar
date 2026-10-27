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

        block.Should().Contain("SELECT post_id FROM comments WHERE id = @parentId AND status = 'active'",
            "the parent comment must be loaded before a reply is attached to it");
        block.Should().Contain("parentPostId != id",
            "the parent comment must belong to the post in the route");
        block.Should().Contain("INVALID_PARENT_COMMENT",
            "a foreign or inactive parent must be rejected with a 400");

        var check = block.IndexOf("INVALID_PARENT_COMMENT", StringComparison.Ordinal);
        var insert = block.IndexOf("INSERT INTO comments", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0);
        check.Should().BeLessThan(insert, "the parent check must run before the comment row is inserted");
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
        var sql = TestRepoPaths.ReadText("backend", "migrations", "V108__comment_parent_same_post.sql");

        sql.Should().Contain("c.post_id <> pc.post_id",
            "already injected cross-post replies must be neutralized");
        sql.Should().Contain("SET status = 'deleted'");
        sql.Should().Contain("pc.post_id = NEW.post_id",
            "the trigger must require the parent comment to be on the same post");
        sql.Should().Contain("BEFORE INSERT OR UPDATE OF parent_id, post_id ON comments");
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
