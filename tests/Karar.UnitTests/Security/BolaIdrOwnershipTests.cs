using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// BOLA/IDOR ownership enforcement — static code-inspection tests.
//
// Every ownership check that relies on a WHERE clause is verified here so a
// future refactor cannot silently drop the ownership predicate.
//
// Coverage matrix:
//   notifications/{id}/read       → device_id ownership in SQL
//   notifications/{id}/dismiss    → device_id ownership in SQL
//   users/me/sessions/{id} DELETE → user_id ownership in SQL
//   comments/{id} DELETE          → device_id OR user_id ownership in SQL
//   comments/{id} PUT             → device_id OR user_id ownership in SQL
//   posts/{id} DELETE             → device_id OR user_id ownership in SQL
//   posts/{id} PUT                → device_id OR user_id ownership in SQL
//   posts/{id}/stats GET          → ownership query before data access
//   posts/{id}/comments/pin POST  → post owner check before pin
//   posts/{id}/comments/pin DELETE→ post owner check before unpin
//
// Content visibility:
//   feed                          → status = 'active'
//   discover feed                 → status = 'active'
//   search                        → status = 'active'
//   single-post GET               → status = 'active'
//   comments list                 → status = 'active'
//   user search                   → deleted_at IS NULL + is_banned check
public sealed class BolaIdrOwnershipTests
{
    // ── Notification ownership ───────────────────────────────────────────────

    [Fact]
    public void NotificationRead_SqlEnforcesDeviceIdOwnership()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapPut(\"/api/v1/notifications/{id:guid}/read\"",
            "app.MapPost(\"/api/v1/notifications/{id:guid}/dismiss\"");

        block.Should().Contain("WHERE id = @id AND device_id = @deviceId",
            "notification read must be scoped to the device that owns it");
    }

    [Fact]
    public void NotificationDismiss_SqlEnforcesDeviceIdOwnership()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapPost(\"/api/v1/notifications/{id:guid}/dismiss\"",
            "app.MapPost(\"/api/v1/notifications/clear-read\"");

        block.Should().Contain("WHERE id = @id AND device_id = @deviceId",
            "notification dismiss must be scoped to the device that owns it");
    }

    // ── Session ownership ────────────────────────────────────────────────────

    [Fact]
    public void DeleteSession_SqlEnforcesUserIdOwnership()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapDelete(\"/api/v1/users/me/sessions/{sessionId:guid}\"",
            "app.MapDelete(\"/api/v1/users/me\"");

        block.Should().Contain("WHERE id = @id AND user_id = @userId",
            "session delete must be scoped to the token's user so user A cannot revoke user B's session");
    }

    // ── Comment ownership ────────────────────────────────────────────────────

    [Fact]
    public void DeleteComment_SqlEnforcesOwnershipViaDeviceOrUser()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapDelete(\"/api/v1/comments/{id:guid}\"",
            "app.MapPut(\"/api/v1/comments/{id:guid}\"");

        block.Should().Contain("device_id = @deviceId OR user_id = @userId",
            "comment delete SQL must check ownership so user A cannot delete user B's comment");
    }

    [Fact]
    public void UpdateComment_SqlEnforcesOwnershipViaDeviceOrUser()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapPut(\"/api/v1/comments/{id:guid}\"",
            "app.MapPost(\"/api/v1/comments/{id:guid}/upvote\"");

        block.Should().Contain("device_id = @deviceId OR user_id = @userId",
            "comment update SQL must check ownership");
    }

    // ── Post ownership ───────────────────────────────────────────────────────

    [Fact]
    public void DeletePost_SqlEnforcesOwnershipViaDeviceOrUser()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapDelete(\"/api/v1/posts/{id:guid}\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/view\"");

        block.Should().Contain("device_id = @deviceId OR user_id = @userId",
            "post delete SQL must check ownership");
    }

    [Fact]
    public void UpdatePost_SqlEnforcesOwnershipViaDeviceOrUser()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapPut(\"/api/v1/posts/{id:guid}\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/save\"");

        block.Should().Contain("device_id = @deviceId OR user_id = @userId",
            "post update SQL must check ownership");
    }

    // ── Post stats: owner-only endpoint ─────────────────────────────────────

    [Fact]
    public void PostStats_ChecksOwnershipBeforeReturningData()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/similar\"");

        block.Should().Contain("device_id = @deviceId OR user_id = @userId",
            "stats endpoint must verify caller owns the post before exposing view/vote metrics");
        // Ownership failure must return 403, not leak data
        block.Should().Contain("NOT_OWNER");
    }

    // ── Comment pin/unpin: post-owner only ───────────────────────────────────

    [Fact]
    public void PinComment_VerifiesPostOwnershipBeforePinning()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments/pin\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/comments/pin\"");

        block.Should().Contain("user_id = @userId",
            "pin endpoint must verify that the caller owns the post");
        block.Should().Contain("NOT_POST_OWNER");
    }

    [Fact]
    public void UnpinComment_VerifiesPostOwnershipBeforeUnpinning()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapDelete(\"/api/v1/posts/{id:guid}/comments/pin\"",
            "app.MapGet(\"/api/v1/users/me/posts\"");

        block.Should().Contain("user_id = @userId",
            "unpin endpoint must verify that the caller owns the post");
        block.Should().Contain("NOT_POST_OWNER");
    }

    // ── Content visibility: hidden/removed/under_review must not surface ─────

    [Fact]
    public void MainFeed_OnlyReturnsActiveContent()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapGet(\"/api/v1/posts\"",
            "app.MapPost(\"/api/v1/posts\"");

        block.Should().Contain("p.status = 'active'",
            "main feed must exclude hidden, removed and under_review posts");
    }

    [Fact]
    public void DiscoverFeed_OnlyReturnsActiveContent()
    {
        var program = ReadProgram();
        var discoverSection = SliceBlock(program,
            "app.MapGet(\"/api/v1/posts/discover/feed\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

        discoverSection.Should().Contain("p.status = 'active'",
            "discover feed must exclude hidden, removed and under_review posts");
    }

    [Fact]
    public void SearchEndpoint_OnlyReturnsActiveContent()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapGet(\"/api/v1/search\"",
            "app.MapGet(\"/api/v1/search/users\"");

        block.Should().Contain("p.status = 'active'",
            "search must not surface hidden, removed or under_review content");
    }

    [Fact]
    public void SinglePostGet_OnlyReturnsActiveContent()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapGet(\"/api/v1/posts/{id:guid}\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/ai-summary\"");

        block.Should().Contain("p.status = 'active'",
            "single post GET must reject requests for hidden/removed/under_review posts");
    }

    [Fact]
    public void CommentsEndpoint_OnlyReturnsActiveComments()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapGet(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"");

        block.Should().Contain("cm.status = 'active'",
            "comment listing must exclude deleted/hidden/under_review comments");
    }

    [Fact]
    public void UserSearch_ExcludesBannedAndDeletedAccounts()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapGet(\"/api/v1/search/users\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"");

        block.Should().Contain("u.deleted_at IS NULL",
            "user search must not surface deleted accounts");
        block.Should().Contain("u.is_banned = FALSE",
            "user search must not surface banned accounts");
    }

    [Fact]
    public void PublicUserPosts_OnlyReturnsActiveContent()
    {
        var program = ReadProgram();
        var block = SliceBlock(program,
            "app.MapGet(\"/api/v1/users/{username}/posts\"",
            "app.MapGet(\"/api/v1/users/{username}/comments\"");

        block.Should().Contain("status = 'active'",
            "public profile posts must not expose hidden/removed content");
    }

    // ── Admin auth order: auth must precede validation ────────────────────────
    // Placing ValidateRequest() before the admin auth check leaks endpoint shape
    // (required fields, enum values) to unauthenticated callers (they get 400
    // not 401). All admin write endpoints must check auth first.

    [Fact]
    public void AdminWriteEndpoints_CheckAuthBeforeValidation()
    {
        var program = ReadProgram();
        // Extract each admin write handler and verify auth appears before ValidateRequest.
        var adminWritePatterns = new[]
        {
            ("app.MapPost(\"/api/v1/admin/reports/{id:guid}/action\"",   "app.MapPost(\"/api/v1/admin/moderation/{targetType}"),
            ("app.MapPost(\"/api/v1/admin/devices/{id:guid}/ban\"",      "app.MapPost(\"/api/v1/admin/devices/{id:guid}/unban\""),
            ("app.MapPost(\"/api/v1/admin/users/{id:guid}/ban\"",        "app.MapPost(\"/api/v1/admin/users/{id:guid}/unban\""),
            ("app.MapPost(\"/api/v1/admin/users/{id:guid}/warn\"",       "app.MapPost(\"/api/v1/admin/users/{id:guid}/strike\""),
            ("app.MapPost(\"/api/v1/admin/users/{id:guid}/strike\"",     "app.MapPost(\"/api/v1/admin/users/{id:guid}/delete\""),
            ("app.MapPost(\"/api/v1/admin/categories/{id:int}/throttle\"","app.MapDelete(\"/api/v1/admin/categories/{id:int}/throttle\""),
        };

        foreach (var (start, end) in adminWritePatterns)
        {
            var block = SliceBlock(program, start, end);
            var authIdx     = block.IndexOf("TryGetAdminEmail", StringComparison.Ordinal);
            var validateIdx = block.IndexOf("ValidateRequest", StringComparison.Ordinal);

            if (validateIdx == -1) continue; // no validation in this endpoint — OK

            authIdx.Should().BeLessThan(validateIdx,
                $"admin endpoint '{start}' must check auth before ValidateRequest — " +
                "returning 400 before 401 leaks endpoint shape to unauthenticated callers");
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static string ReadProgram() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end   = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker '{startMarker}' must exist in Program.cs");
        end.Should().BeGreaterThan(start, $"end marker '{endMarker}' must appear after '{startMarker}'");

        return text[start..end];
    }
}
