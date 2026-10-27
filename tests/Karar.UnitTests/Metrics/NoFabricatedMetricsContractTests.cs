using FluentAssertions;

namespace Karar.UnitTests.Metrics;

/// <summary>
/// Kullanıcıya gösterilen metrikler (oy zaman çizelgesi, trend büyüme yüzdesi)
/// veritabanından türetilmeli; Program.cs içinde Random.Shared yalnızca
/// oy zamanlama jitter'ı için kullanılabilir.
/// </summary>
public sealed class NoFabricatedMetricsContractTests
{
    private const string AllowedJitterLine =
        "return Task.Delay(Random.Shared.Next(5, 51), cancellationToken);";

    [Fact]
    public void ProgramCs_UsesRandomSharedOnlyForVoteTimingJitter()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var randomLines = programText
            .Split('\n')
            .Select((line, index) => (Number: index + 1, Line: line.TrimEnd('\r').Trim()))
            .Where(entry => entry.Line.Contains("Random.Shared", StringComparison.Ordinal))
            .ToList();

        randomLines.Should().ContainSingle(
            "Random.Shared is allowed only for the vote timing jitter; user-facing metrics must come from the database. Offending lines: {0}",
            string.Join("; ", randomLines.Select(entry => $"{entry.Number}: {entry.Line}")));
        randomLines[0].Line.Should().Be(AllowedJitterLine);
    }

    [Fact]
    public void PostStatsEndpoint_DerivesVoteTimelineFromVotesTable()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        programText.Should().NotContain("Mock vote timeline");
        programText.Should().Contain("VoteTimelineBuilder.ZeroFillHourly(");
        programText.Should().Contain("date_trunc('hour', created_at AT TIME ZONE 'UTC')");
    }

    [Fact]
    public void TrendTopicsEndpoint_DerivesGrowthFromPostWindows()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        programText.Should().NotContain("Simplified growth metric");
        programText.Should().Contain("growthPercent = TrendGrowthCalculator.Percent(recentCount, previousCount)");
    }
}
