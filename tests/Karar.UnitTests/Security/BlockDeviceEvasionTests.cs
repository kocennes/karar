using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

/// Regression tests: a user blocked by a post author must not escape the block by
/// dropping the Authorization header and acting with the same X-Device-Token only.
///
/// The block checks are raw SQL inside Program.cs / CommentNotificationBatcher and
/// need Postgres, so these are source-contract tests over the handlers and SQL.
public sealed class BlockDeviceEvasionTests
{
    [Fact]
    public void VoteAndComment_RunBlockCheckForDeviceOnlyRequests()
    {
        var program = ReadProgram();
        var vote = Slice(program,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");
        var comment = Slice(program,
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapDelete(\"/api/v1/comments/{id:guid}\"");

        foreach (var handler in new[] { vote, comment })
        {
            handler.Should().NotContain("userId is not null && await IsBlockedByPostAuthorAsync",
                "a JWT-less request (userId == null) must still be checked against blocks");
            handler.Should().Contain(
                "IsBlockedByPostAuthorAsync(connection, transaction, id, userId, effectiveDeviceId.Value)");
        }
    }

    [Fact]
    public void IsBlockedByPostAuthor_ResolvesDeviceOnlyActorThroughLinkedAccounts()
    {
        var helper = Slice(ReadProgram(),
            "static async Task<bool> IsBlockedByPostAuthorAsync",
            "static async Task<(CreatePostRequest? Request");

        helper.Should().Contain("Guid? actorUserId");
        helper.Should().Contain("Guid actorDeviceId");
        helper.Should().Contain("bu.blocked_user_id = @actorUserId");
        helper.Should().Contain("user_device_links udl");
        helper.Should().Contain("udl.device_id = @actorDeviceId");
        helper.Should().Contain("udl.user_id = bu.blocked_user_id");
        // A device shared with the blocker never blocks the blocker's own guest actions.
        helper.Should().Contain("own.user_id = bu.blocker_user_id");
    }

    [Fact]
    public void CommentList_HidesDeviceOnlyCommentsFromBlockedUsersDevices()
    {
        var list = Slice(ReadProgram(),
            "app.MapGet(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"");

        list.Should().Contain("bu.blocked_user_id = cm.user_id");
        list.Should().Contain("cm.user_id IS NULL");
        list.Should().Contain("udl.device_id = cm.device_id");
        // count, top-level and both recursive branches must use the same filter
        CountOccurrences(list, "{blockedCommentAuthorFilter}").Should().Be(4);
    }

    [Fact]
    public void CommentNotifications_SuppressedForDeviceOnlyCommentsFromBlockedUsers()
    {
        var batcher = TestRepoPaths.ReadText(
            "backend", "Karar.Api", "Services", "CommentNotificationBatcher.cs");

        batcher.Should().NotContain("commenterUserId is not null && await IsBlockedByPostOwnerAsync");
        batcher.Should().Contain("IsBlockedByPostOwnerAsync(postId, commenterUserId, commenterDeviceId)");

        var ownerCheck = Slice(batcher,
            "private async Task<bool> IsBlockedByPostOwnerAsync",
            "private async Task<bool> ShouldNotifyAsync");
        ownerCheck.Should().Contain("user_device_links udl");
        ownerCheck.Should().Contain("udl.device_id = @commenterDeviceId");

        var replyCheck = Slice(batcher,
            "private async Task NotifyReplyAuthorAsync",
            "protected override async Task ExecuteAsync");
        replyCheck.Should().NotContain("if (commenterUserId is not null && parentAuthorUserId is not null)");
        replyCheck.Should().Contain("user_device_links udl");
        replyCheck.Should().Contain("udl.device_id = @commenterDeviceId");
    }

    [Fact]
    public void AccountDevices_AreLinkedOnLoginRegistrationAndAuthenticatedComments()
    {
        var program = ReadProgram();

        Slice(program, "app.MapPost(\"/api/v1/auth/login\"", "app.MapPost(\"/api/v1/auth/refresh\"")
            .Should().Contain("LinkUserDeviceAsync(connection, transaction, userId, deviceId.Value)");
        Slice(program, "app.MapPost(\"/api/v1/auth/google\"", "app.MapPost(\"/api/v1/auth/google/link\"")
            .Should().Contain("LinkUserDeviceAsync(connection, transaction, userId, deviceId.Value)");
        Slice(program, "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"", "app.MapDelete(\"/api/v1/comments/{id:guid}\"")
            .Should().Contain("LinkUserDeviceAsync(connection, transaction, userId.Value, deviceId.Value)");
        Slice(program, "app.MapPost(\"/api/v1/auth/register\"", "app.MapPost(\"/api/v1/auth/verify-email\"")
            .Should().Contain("INSERT INTO user_device_links (user_id, device_id)");

        var helper = Slice(program, "static async Task LinkUserDeviceAsync", "static async Task<bool> IsPostOwnerAsync");
        helper.Should().Contain("INSERT INTO user_device_links (user_id, device_id)");
        helper.Should().Contain("ON CONFLICT (user_id, device_id) DO NOTHING");
    }

    [Fact]
    public void Migration_CreatesUserDeviceLinksAndBackfillsKnownDevices()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V112__user_device_links.sql");

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS user_device_links");
        migration.Should().Contain("REFERENCES users(id) ON DELETE CASCADE");
        migration.Should().Contain("REFERENCES devices(id) ON DELETE CASCADE");
        migration.Should().Contain("PRIMARY KEY (user_id, device_id)");
        migration.Should().Contain("ON user_device_links(device_id)");
        // registration device + devices already used while authenticated
        migration.Should().Contain("SELECT u.id, u.device_id");
        migration.Should().Contain("FROM comments c");
        migration.Should().Contain("FROM votes v");
        migration.Should().Contain("FROM posts p");
    }

    private static string ReadProgram() => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0;
             i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"missing start marker {start}");
        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"missing end marker {end}");
        return text[startIndex..endIndex];
    }
}
