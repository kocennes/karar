using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

// Regression: GET /users/{username}/posts returned the user's anonymous posts (only the
// author name was blanked), so the author's public profile listed and counted them.
public sealed class PublicUserPostsAnonymityTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void PublicUserPosts_DoesNotListOrCountAnonymousPosts()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/posts\"",
            "app.MapGet(\"/api/v1/users/me/comments\"");

        // Kept implementation (sec/01): anonymous posts are listed/counted only when the
        // authenticated viewer is the owner; every other viewer gets is_anonymous = FALSE only.
        block.Should().Contain(
            "SELECT COUNT(*) FROM posts WHERE user_id = @ownerId AND status = 'active' AND (is_anonymous = FALSE OR user_id = @viewerId)",
            "the public total must not count anonymous posts");
        block.Should().Contain("countCmd.Parameters.AddWithValue(\"viewerId\", userParam);",
            "the owner exemption must be keyed on the JWT user, never on a client-supplied value");
        block.Should().MatchRegex(
            @"WHERE p\.user_id = @ownerId AND p\.status = 'active'\s+AND \(p\.is_anonymous = FALSE OR p\.user_id = @userParam\)",
            "listing an anonymous post under its author's public profile de-anonymizes it outright");
        block.Should().Contain("var userParam = userId ?? Guid.Empty;",
            "a guest viewer (no JWT) must never match an owner id");
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
