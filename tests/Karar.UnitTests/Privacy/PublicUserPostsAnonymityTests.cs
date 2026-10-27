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

        block.Should().Contain(
            "SELECT COUNT(*) FROM posts WHERE user_id = @ownerId AND status = 'active' AND is_anonymous = FALSE",
            "the public total must not count anonymous posts");
        block.Should().Contain("WHERE p.user_id = @ownerId AND p.status = 'active' AND p.is_anonymous = FALSE",
            "listing an anonymous post under its author's public profile de-anonymizes it outright");
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
