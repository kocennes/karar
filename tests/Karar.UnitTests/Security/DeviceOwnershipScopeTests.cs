using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Content created by a logged-in account also stores the request's device_id, and the client keeps
// X-Device-Token after logout. A "device_id = @deviceId OR user_id = @userId" ownership test
// therefore hands an account's posts/comments to whoever uses that device next (shared phone or
// browser): isOwner=true on the previous user's anonymous posts, delete/edit, owner stats.
// Device ownership may only apply to guest content (user_id IS NULL).
public sealed class DeviceOwnershipScopeTests
{
    private const string GuestOrAccountOwner = "(CASE WHEN user_id IS NULL THEN device_id = @deviceId ELSE user_id = @userId END)";

    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly Regex LenientOwnership = new(
        @"\(\s*(?:\w+\.)?device_id\s*=\s*@deviceId\s+OR\s+(?:\w+\.)?user_id\s*=\s*@userId\s*\)" +
        @"|\(\s*(?:\w+\.)?user_id\s*=\s*@userId\s+OR\s+(?:\w+\.)?device_id\s*=\s*@deviceId\s*\)",
        RegexOptions.Compiled);

    [Fact]
    public void Program_HasNoDeviceOrUserOwnershipShortcut()
    {
        var matches = LenientOwnership.Matches(ProgramText)
            .Select(m => $"line {LineOf(m.Index)}: {m.Value}")
            .ToList();

        matches.Should().BeEmpty(
            "a device match must not grant ownership of content that belongs to an account");
    }

    [Theory]
    [InlineData("app.MapDelete(\"/api/v1/posts/{id:guid}\"", "app.MapPost(\"/api/v1/posts/{id:guid}/view\"")]
    [InlineData("app.MapPut(\"/api/v1/posts/{id:guid}\"", "app.MapPost(\"/api/v1/posts/{id:guid}/save\"")]
    [InlineData("app.MapDelete(\"/api/v1/comments/{id:guid}\"", "app.MapPut(\"/api/v1/comments/{id:guid}\"")]
    [InlineData("app.MapPut(\"/api/v1/comments/{id:guid}\"", "app.MapPost(\"/api/v1/comments/{id:guid}/upvote\"")]
    [InlineData("app.MapGet(\"/api/v1/posts/{id:guid}/stats\"", "app.MapGet(\"/api/v1/posts/{id:guid}/similar\"")]
    [InlineData("app.MapPost(\"/api/v1/users/me/moderation-appeals\"", "app.MapPost(\"/api/v1/users/me/blocked\"")]
    public void OwnerOnlyEndpoints_AllowDeviceOwnershipOnlyForGuestContent(string start, string end)
    {
        var block = Slice(ProgramText, start, end);

        block.Should().Contain(GuestOrAccountOwner,
            "account content may only be managed by the account (JWT), guest content by its device");
    }

    [Fact]
    public void IsOwnerFlags_UseAccountScopedOwnership()
    {
        // CASE (not "a OR b") also keeps the flag non-NULL for guest rows, which the readers
        // (ReadCommentRow, ReadPostsWithTotalAsync) read with GetBoolean.
        CountOccurrences(ProgramText, "(CASE WHEN p.user_id IS NULL THEN p.device_id = @deviceId ELSE p.user_id = @userId END)")
            .Should().BeGreaterThanOrEqualTo(19, "every feed/detail/search isOwner flag must be account scoped");

        var listColumns = Slice(ProgramText, "const string commentSelectColumns", "static CommentDto ReadCommentRow");
        listColumns.Should().Contain("(CASE WHEN cm.user_id IS NULL THEN cm.device_id = @deviceId ELSE cm.user_id = @userId END)");

        var singleComment = Slice(
            ProgramText,
            "static async Task<CommentDto?> ReadCommentDtoAsync",
            "static async Task<IResult> SetCommentUpvoteAsync");
        singleComment.Should().Contain("(CASE WHEN cm.user_id IS NULL THEN cm.device_id = @deviceId ELSE cm.user_id = @userId END)");
    }

    [Fact]
    public void SelfVoteGuard_IsAccountScopedForLoggedInVoters()
    {
        var helper = Slice(
            ProgramText,
            "static async Task<bool> IsPostOwnerAsync",
            "static async Task<bool> IsBlockedByPostAuthorAsync");

        helper.Should().Contain(GuestOrAccountOwner,
            "a different logged-in account on a shared device is not the author of the previous user's post");
        helper.Should().Contain("(@isGuest AND device_id = @deviceId)",
            "a logged-out caller on the author's device is still treated as the author, so self-voting stays blocked");
        helper.Should().Contain("command.Parameters.AddWithValue(\"isGuest\", userId is null)");
    }

    private static int LineOf(int index) => ProgramText[..index].Count(c => c == '\n') + 1;

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var idx = text.IndexOf(value, StringComparison.Ordinal);
             idx >= 0;
             idx = text.IndexOf(value, idx + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
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
