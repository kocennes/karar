using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

// Regression: GET /users/{username}/comments listed the user's replies inside their own
// anonymous posts (the same comments are flagged isPostOwner=true in the thread), which
// names the anonymous author. The handler also selected a column no migration created.
public sealed class PublicUserCommentsPrivacyTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void PublicUserComments_ExcludeOpCommentsOnOwnAnonymousPosts()
    {
        var block = Slice(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/comments\"",
            "app.MapGet(\"/api/v1/users/me/karma-history\"");

        Regex.Matches(block, Regex.Escape("AND NOT (p.is_anonymous AND p.user_id IS NOT DISTINCT FROM cm.user_id)"))
            .Count.Should().Be(2,
                "both the total and the list must hide the user's comments on their own anonymous posts");
    }

    [Fact]
    public void PublicUserComments_ProfileVisibilityColumnIsCreatedByAMigration()
    {
        ProgramText.Should().Contain("is_profile_visible");

        var migrationsDir = TestRepoPaths.FilePath("backend", "migrations");
        var definesColumn = Directory.GetFiles(migrationsDir, "V*.sql")
            .Select(File.ReadAllText)
            .Any(sql => sql.Contains("ADD COLUMN IF NOT EXISTS is_profile_visible", StringComparison.Ordinal));

        definesColumn.Should().BeTrue(
            "GET /users/{username}/comments selects users.is_profile_visible; without the column it fails with a 500");
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
