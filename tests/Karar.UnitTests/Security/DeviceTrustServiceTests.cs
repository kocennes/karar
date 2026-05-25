using FluentAssertions;
using Karar.UnitTests;
using Karar.Api.Services;

namespace Karar.UnitTests.Security;

public sealed class DeviceTrustServiceTests
{
    [Fact]
    public void CalculateScore_RewardsOlderBroadNormalDevices()
    {
        var score = DeviceTrustService.CalculateScore(new DeviceTrustSignals(
            DeviceAge: TimeSpan.FromDays(8),
            FailedIntegrityCount: 0,
            ReportAbuseCount: 0,
            VoteBreadthCount: 4,
            RecentVoteCount: 2
        ));

        score.Should().Be(0.7);
    }

    [Fact]
    public void CalculateScore_PenalizesFailedIntegrityBelowSuspiciousThreshold()
    {
        var score = DeviceTrustService.CalculateScore(new DeviceTrustSignals(
            DeviceAge: TimeSpan.FromMinutes(20),
            FailedIntegrityCount: 1,
            ReportAbuseCount: 0,
            VoteBreadthCount: 0,
            RecentVoteCount: 0
        ));

        score.Should().BeLessThan(DeviceTrustService.SuspiciousThreshold);
    }

    [Fact]
    public void VoteEndpoint_QuarantinesSuspiciousVotesWithoutBlockingVisibleCounts()
    {
        var programText = ReadProgram();
        var voteBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        voteBlock.Should().Contain("EvaluateForVoteAsync");
        voteBlock.Should().Contain("trustDecision.ShouldQuarantineVote");
        voteBlock.Should().Contain("return Results.Ok(response);");
    }

    [Fact]
    public void TrendScoreQueries_ExcludeQuarantinedVotes()
    {
        var programText = ReadProgram();
        var trendUpdaterText = ReadFile("backend", "Karar.Api", "Services", "TrendScoreUpdater.cs");

        programText.Should().Contain("v.is_quarantined = FALSE");
        trendUpdaterText.Should().Contain("v.is_quarantined = FALSE");
    }

    private static string ReadProgram() => ReadFile("backend", "Karar.Api", "Program.cs");

    private static string ReadFile(params string[] pathParts)
    {
        return TestRepoPaths.ReadText(pathParts);
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }

}
