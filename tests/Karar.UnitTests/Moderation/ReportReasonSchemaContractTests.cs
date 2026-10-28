using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

/// <summary>
/// reports.reason CHECK (son migration) ↔ POST /api/v1/reports allowlist (Program.cs) ↔ ReportBottomSheet (Flutter).
/// API'nin kabul edip veritabanının reddettiği bir sebep kullanıcıya 500 olarak döner
/// ("Yanlış Bilgi" / misinformation hatası); bu test üç listeyi küme olarak eşit tutar.
/// </summary>
public sealed class ReportReasonSchemaContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly string ReportBottomSheetText =
        TestRepoPaths.ReadText("lib", "features", "report", "report_bottom_sheet.dart");

    // V1 form: CREATE TABLE reports ( ... reason TEXT NOT NULL CHECK (reason IN ('a', 'b')) ... )
    private static readonly Regex CreateTableReasonCheck = new(
        @"CREATE\s+TABLE\s+reports\s*\([^;]*?CHECK\s*\(\s*reason\s+IN\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // Later form: ALTER TABLE reports ADD CONSTRAINT reports_reason_check CHECK (reason IN ('a', 'b'))
    private static readonly Regex AlterTableReasonCheck = new(
        @"ALTER\s+TABLE\s+reports\s+ADD\s+CONSTRAINT\s+\w+\s+CHECK\s*\(\s*reason\s+IN\s*\(([^)]*)\)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    [Fact]
    public void LatestReportsReasonCheck_MatchesProgramAllowlist()
    {
        var (migrationName, dbReasons) = LatestReasonCheckMigration();
        var apiReasons = ProgramAllowlist();

        apiReasons.Should().NotBeEmpty();
        dbReasons.Should().BeEquivalentTo(apiReasons,
            because: $"{migrationName} is the latest migration defining the reports.reason CHECK; " +
                     "a reason accepted by POST /api/v1/reports but rejected by the CHECK fails the INSERT " +
                     "and the user sees a 500 — add a new migration that re-creates reports_reason_check");
    }

    [Fact]
    public void ReportBottomSheetReasons_MatchProgramAllowlist()
    {
        var uiReasons = Regex.Matches(ReportBottomSheetText, @"\(\s*'([a-z_]+)'\s*,\s*'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        uiReasons.Should().BeEquivalentTo(ProgramAllowlist(),
            because: "every reason the report sheet offers must be accepted by the API, and vice versa");
    }

    [Fact]
    public void TransparencyReasonBuckets_UseOnlyAllowlistedReasons()
    {
        var transparencyBlock = SliceBlock(
            ProgramText,
            "AS reason_harassment",
            "AS reason_other");

        var bucketReasons = Regex.Matches(transparencyBlock, "'([a-z_]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        bucketReasons.Should().NotBeEmpty();
        bucketReasons.Should().BeSubsetOf(ProgramAllowlist(),
            because: "the public moderation transparency report must only count reasons the API can store");
        bucketReasons.Should().Contain("misinformation");
    }

    private static string ReportEndpointBlock => SliceBlock(
        ProgramText,
        "app.MapPost(\"/api/v1/reports\"",
        "app.MapPost(\"/api/v1/feedback\"");

    private static HashSet<string> ProgramAllowlist()
    {
        // Scoped to the report endpoint: other endpoints (e.g. feed feedback) have their own request.Reason allowlists.
        var match = Regex.Match(ReportEndpointBlock, @"request\.Reason\s+is\s+not\s+\(([^)]*)\)");
        match.Success.Should().BeTrue("POST /api/v1/reports must validate request.Reason against an allowlist");

        return Regex.Matches(match.Groups[1].Value, "\"([a-z_]+)\"")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static (string Name, HashSet<string> Reasons) LatestReasonCheckMigration()
    {
        var migrationsDir = TestRepoPaths.FilePath("backend", "migrations");

        var defining = Directory.GetFiles(migrationsDir, "V*.sql")
            .Select(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                var version = int.TryParse(name.TrimStart('V', 'v').Split('_')[0], out var n) ? n : int.MaxValue;
                return (Name: name, Version: version, Reasons: ExtractReasonCheck(File.ReadAllText(path)));
            })
            .Where(m => m.Reasons is not null)
            .OrderBy(m => m.Version)
            .ThenBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        defining.Should().NotBeEmpty("at least V1 defines the reports.reason CHECK constraint");

        var latest = defining[^1];
        return (latest.Name, latest.Reasons!);
    }

    private static HashSet<string>? ExtractReasonCheck(string sql)
    {
        var lastMatch = CreateTableReasonCheck.Matches(sql)
            .Concat(AlterTableReasonCheck.Matches(sql))
            .OrderBy(m => m.Index)
            .LastOrDefault();

        if (lastMatch is null)
        {
            return null;
        }

        return Regex.Matches(lastMatch.Groups[1].Value, "'([a-z_]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
