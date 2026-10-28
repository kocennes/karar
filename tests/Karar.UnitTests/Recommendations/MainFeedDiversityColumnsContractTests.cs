using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

// GET /api/v1/posts, first page of the default trending feed, runs the diversity pass: a CTE named
// "base" and an outer SELECT that reads its columns by name. The ownership expression in the CTE had
// lost its alias, so Postgres refused the outer "is_owner" (42703) and the home feed returned 500 on
// every first load. Every column the outer SELECT names must be produced by the CTE under that name.
public sealed class MainFeedDiversityColumnsContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void DiversityPassOuterSelect_OnlyReadsColumnsTheBaseCteNames()
    {
        // The count query above it has its own "WITH base AS (" CTE; start at the feed query.
        var feedSql = ProgramText.IndexOf("feedSql = $\"\"\"", StringComparison.Ordinal);
        feedSql.Should().BeGreaterThanOrEqualTo(0, "the main feed builds feedSql");
        var from = ProgramText.IndexOf("WITH base AS (", feedSql, StringComparison.Ordinal);
        from.Should().BeGreaterThanOrEqualTo(0, "the main feed diversity pass uses a CTE named base");
        var to = ProgramText.IndexOf("var freshSlotTarget = applyDiversityPass", from, StringComparison.Ordinal);
        to.Should().BeGreaterThan(from);
        var block = ProgramText[from..to];

        var innerStart = block.IndexOf("SELECT", StringComparison.Ordinal);
        var innerEnd = block.IndexOf("FROM posts p", innerStart, StringComparison.Ordinal);
        var inner = block[innerStart..innerEnd];

        var outerEnd = block.IndexOf("FROM base", StringComparison.Ordinal);
        outerEnd.Should().BeGreaterThan(innerEnd, "the outer query reads from the base CTE");
        var outerStart = block.LastIndexOf("SELECT", outerEnd, StringComparison.Ordinal);
        var outerColumns = block[(outerStart + "SELECT".Length)..outerEnd]
            .Split(',')
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .ToList();

        outerColumns.Should().Contain("is_owner");
        foreach (var column in outerColumns)
        {
            var named = new Regex($@"(\b[pcv]\.{column}\b(?!\s+AS\s)|\bAS\s+{column}\b)");
            named.IsMatch(inner).Should().BeTrue(
                $"the outer SELECT reads \"{column}\", so the base CTE must select it under that name");
        }
    }
}
