using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class BlockSemanticsVisibilityTests
{
    // ── Interaction suppression (reverse direction) ────────────────────────

    [Fact]
    public void VoteEndpoint_RejectsVoteWhenActorIsBlockedByPostAuthor()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var voteBlock = Slice(
            program,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        voteBlock.Should().Contain("IsBlockedByPostAuthorAsync",
            "vote endpoint must reject registered users blocked by the post author");
        voteBlock.Should().Contain("BLOCKED_BY_AUTHOR",
            "vote endpoint must return BLOCKED_BY_AUTHOR error code");
    }

    [Fact]
    public void CommentEndpoint_RejectsCommentWhenActorIsBlockedByPostAuthor()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var commentBlock = Slice(
            program,
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapPost(\"/api/v1/comments/{id:guid}/upvote\"");

        commentBlock.Should().Contain("IsBlockedByPostAuthorAsync",
            "comment endpoint must reject registered users blocked by the post author");
        commentBlock.Should().Contain("BLOCKED_BY_AUTHOR",
            "comment endpoint must return BLOCKED_BY_AUTHOR error code");
    }

    [Fact]
    public void IsBlockedByPostAuthorHelper_QueriesBlockedUsersReverseLookup()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var helperBlock = Slice(
            program,
            "static async Task<bool> IsBlockedByPostAuthorAsync",
            "static async Task<(CreatePostRequest? Request");

        helperBlock.Should().Contain("blocked_users bu",
            "helper must query blocked_users table");
        helperBlock.Should().Contain("bu.blocker_user_id",
            "helper must filter by who placed the block (post author)");
        helperBlock.Should().Contain("bu.blocked_user_id = @actorUserId",
            "helper must check that the actor is the blocked party");
    }

    [Fact]
    public void CommentNotificationBatcher_SuppressesNotificationWhenActorIsBlockedByOwner()
    {
        var batcher = TestRepoPaths.ReadText(
            "backend", "Karar.Api", "Services", "CommentNotificationBatcher.cs");

        batcher.Should().Contain("IsBlockedByPostOwnerAsync",
            "batcher must suppress notifications from users blocked by the post owner");
        batcher.Should().Contain("commenterUserId",
            "batcher must accept and use the commenter's userId for block lookup");
        batcher.Should().Contain("bu.blocked_user_id = @commenterUserId",
            "block check must compare against the commenter userId");
    }

    [Fact]
    public void CommentNotificationBatcher_SuppressesReplyNotificationWhenActorIsBlockedByParentAuthor()
    {
        var batcher = TestRepoPaths.ReadText(
            "backend", "Karar.Api", "Services", "CommentNotificationBatcher.cs");

        var replyBlock = Slice(
            batcher,
            "private async Task NotifyReplyAuthorAsync",
            "protected override async Task ExecuteAsync");

        replyBlock.Should().Contain("blocker_user_id = @parentAuthorUserId",
            "reply notification must be suppressed when parent author has blocked the replier");
        replyBlock.Should().Contain("blocked_user_id = @commenterUserId");
    }

    // ── Visibility filtering (original direction) ──────────────────────────

    [Fact]
    public void BlockedAuthors_AreHiddenFromPostDetailCommentsNotificationsAndSearch()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        Slice(program, "app.MapGet(\"/api/v1/posts/{id:guid}\"", "app.MapPost(\"/api/v1/posts/{id:guid}/ai-summary\"")
            .Should().Contain("blocked_users bu");

        var commentsBlock = Slice(
            program,
            "app.MapGet(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"");
        commentsBlock.Should().Contain("blocked_users bu");
        commentsBlock.Should().Contain("bu.blocked_user_id = cm.user_id");
        commentsBlock.Should().Contain("SELECT COUNT(*)");

        var notificationsBlock = Slice(
            program,
            "app.MapGet(\"/api/v1/notifications\"",
            "app.MapPut(\"/api/v1/notifications/read-all\"");
        notificationsBlock.Should().Contain("SELECT id FROM users WHERE device_id = @deviceId");
        notificationsBlock.Should().Contain("LEFT JOIN comments nc ON nc.id = NULLIF(n.payload->>'comment_id', '')::uuid");
        notificationsBlock.Should().Contain("bu.blocked_user_id = p.user_id");
        notificationsBlock.Should().Contain("bu.blocked_user_id = nc.user_id");

        var searchBlock = Slice(
            program,
            "app.MapGet(\"/api/v1/search\"",
            "app.MapGet(\"/api/v1/search/users\"");
        searchBlock.Should().Contain("blocked_users bu");
        searchBlock.Should().Contain("bu.blocked_user_id = p.user_id");

        var userSearchBlock = Slice(
            program,
            "app.MapGet(\"/api/v1/search/users\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"");
        userSearchBlock.Should().Contain("GetOptionalUserId(httpRequest, jwtService)");
        userSearchBlock.Should().Contain("blocked_users bu");
        userSearchBlock.Should().Contain("bu.blocked_user_id = u.id");
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);
        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex);
        return text[startIndex..endIndex];
    }
}
