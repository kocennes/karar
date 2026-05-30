using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

public sealed class AnonymousPostContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void MainFeedDiversityPass_SelectsAnonymityWithoutConfusingOwnership()
    {
        var block = SliceBlock(
            ProgramText,
            "WITH base AS (",
            "var freshSlotTarget = applyDiversityPass");

        block.Should().NotContain("p.is_anonymous AS is_owner",
            "anonymity and ownership are separate concepts and must not share the same alias");
        block.Should().Contain("vote_type, trend_score, created_at, is_owner, is_anonymous",
            "page-1 diversity feed rows must still expose is_anonymous so ReadPostsAsync redacts authors");
    }

    [Fact]
    public void PublicUserPosts_RedactsAnonymousAuthorAndSelectsAnonymityFlag()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/posts\"",
            "app.MapGet(\"/api/v1/users/{username}/comments\"");

        block.Should().Contain("CASE WHEN p.is_anonymous THEN NULL ELSE u.username END",
            "public profile post cards must not reveal the real author name for anonymous posts");
        block.Should().Contain("p.is_anonymous",
            "clients need the anonymity flag to render the anonymous author affordance");
    }

    [Fact]
    public void ReadPostsAsync_UsesColumnNamesForOptionalFields()
    {
        var block = SliceBlock(
            ProgramText,
            "static async Task<IReadOnlyList<PostDto>> ReadPostsAsync",
            "static async Task<(IReadOnlyList<PostDto> Posts, int Total)> ReadPostsWithTotalAsync");

        block.Should().Contain("ReadBool(\"is_unlisted\")",
            "optional fields appear in different query shapes and must not depend on fragile column counts");
        block.Should().Contain("ReadBool(\"is_anonymous\")",
            "anonymous redaction must work for every query that selects the flag");
        block.Should().Contain("ReadString(\"status\")",
            "my-posts queries include status where feed queries include is_unlisted");
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }
}
