using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// A reply's parent must belong to the same post. Without that binding, a caller can create a
// reply on their own post X whose parent is a comment on victim post Y: the thread query of Y
// then picks the reply up by parent_id alone and computes is_post_owner against X, so the
// injected reply renders as the anonymous OP ('@anonim [Yargilanan]') inside Y.
public sealed class CommentThreadIntegrityTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string CreateCommentBlock => Slice(
        ProgramText,
        "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"",
        "app.MapDelete(\"/api/v1/comments/{id:guid}\"");

    [Fact]
    public void CreateComment_RejectsParentThatBelongsToAnotherPost()
    {
        var block = CreateCommentBlock;
        var parentCheck = block.IndexOf(
            "SELECT EXISTS(SELECT 1 FROM comments WHERE id = @parentId AND post_id = @postId AND status = 'active')",
            StringComparison.Ordinal);
        var insert = block.IndexOf("INSERT INTO comments", StringComparison.Ordinal);

        parentCheck.Should().BeGreaterThan(0, "the parent comment must be looked up and bound to the path post");
        insert.Should().BeGreaterThan(parentCheck, "the parent check must run before the reply is inserted");
        block.Should().Contain("INVALID_PARENT");
    }

    [Fact]
    public void CreateComment_RequiresActiveTargetPost()
    {
        var block = CreateCommentBlock;
        var postCheck = block.IndexOf(
            "SELECT EXISTS(SELECT 1 FROM posts WHERE id = @postId AND status = 'active')",
            StringComparison.Ordinal);
        var insert = block.IndexOf("INSERT INTO comments", StringComparison.Ordinal);

        postCheck.Should().BeGreaterThan(0, "deleted/hidden posts must not accept new comments");
        insert.Should().BeGreaterThan(postCheck);
        block.Should().Contain("NotFound(\"POST_NOT_FOUND\"");
    }

    [Fact]
    public void CommentThread_DescendantsAreBoundToTheRequestedPost()
    {
        var cte = Slice(ProgramText, "WITH RECURSIVE descendants AS (", "SELECT * FROM descendants");
        var unionAt = cte.IndexOf("UNION ALL", StringComparison.Ordinal);
        unionAt.Should().BeGreaterThan(0);

        cte[..unionAt].Should().Contain("cm.post_id = @postId",
            "direct replies of the visible top-level comments must come from the same post");
        cte[unionAt..].Should().Contain("cm.post_id = @postId",
            "nested replies must come from the same post");
    }

    [Fact]
    public void Migration_BindsReplyParentToTheSamePost()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V100__comment_parent_same_post.sql");

        migration.Should().Contain("ADD CONSTRAINT comments_post_id_id_key UNIQUE (post_id, id)");
        migration.Should().Contain("FOREIGN KEY (post_id, parent_id) REFERENCES comments (post_id, id)",
            "the database must reject a reply whose parent lives on another post");
        migration.Should().Contain("NOT VALID",
            "legacy rows must not make the deploy-time migration fail");
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
