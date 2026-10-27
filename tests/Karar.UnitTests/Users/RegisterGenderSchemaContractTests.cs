using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Users;

/// <summary>
/// users.gender CHECK (V31) ↔ POST /api/v1/auth/register allowlist ↔ register screen dropdown.
/// A gender the API forwards but the CHECK rejects fails the INSERT with 23514 and the user sees a 500.
/// </summary>
public sealed class RegisterGenderSchemaContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void RegisterGenderAllowlist_MatchesUsersGenderCheck()
    {
        var migrationSql = TestRepoPaths.ReadText("backend", "migrations", "V31__user_profile_fields.sql");
        var check = Regex.Match(migrationSql, @"gender\s+TEXT\s+CHECK\s*\(\s*gender\s+IN\s*\(([^)]*)\)", RegexOptions.IgnoreCase);
        check.Success.Should().BeTrue("V31 defines the users.gender CHECK");
        var dbValues = Quoted(check.Groups[1].Value, '\'');

        var register = SliceBlock(ProgramText, "app.MapPost(\"/api/v1/auth/register\"", "app.MapPost(\"/api/v1/auth/verify-email\"");
        var allowlist = Regex.Match(register, @"request\.Gender\s+is\s+not\s+\(([^)]*)\)");
        allowlist.Success.Should().BeTrue("register must validate request.Gender before the INSERT");
        var apiValues = Quoted(allowlist.Groups[1].Value, '"');

        apiValues.Should().BeEquivalentTo(dbValues);
        register.IndexOf("request.Gender is not", StringComparison.Ordinal)
            .Should().BeLessThan(register.IndexOf("INSERT INTO users", StringComparison.Ordinal));
    }

    [Fact]
    public void RegisterScreenGenders_AreAcceptedByTheApi()
    {
        var screen = TestRepoPaths.ReadText("lib", "features", "auth", "register_screen.dart");
        var uiValues = Regex.Matches(screen, @"DropdownMenuItem\(\s*value:\s*'([a-z_]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var register = SliceBlock(ProgramText, "app.MapPost(\"/api/v1/auth/register\"", "app.MapPost(\"/api/v1/auth/verify-email\"");
        var apiValues = Quoted(Regex.Match(register, @"request\.Gender\s+is\s+not\s+\(([^)]*)\)").Groups[1].Value, '"');

        uiValues.Should().NotBeEmpty();
        uiValues.Should().BeSubsetOf(apiValues);
    }

    private static HashSet<string> Quoted(string list, char quote) =>
        Regex.Matches(list, $"{quote}([a-z_]+){quote}")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
