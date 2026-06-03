using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class VoteEndpointSecurityTests
{
    // ── Timing jitter ──────────────────────────────────────────────────────

    [Fact]
    public void VoteEndpoint_AddsSmallRandomTimingJitterBeforeBranching()
    {
        var programText = ReadProgram();
        var voteBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        voteBlock.Should().Contain("await AddVoteTimingJitterAsync(httpRequest.HttpContext.RequestAborted);");
        voteBlock.IndexOf("AddVoteTimingJitterAsync", StringComparison.Ordinal)
            .Should().BeLessThan(voteBlock.IndexOf("ValidateRequest(request)", StringComparison.Ordinal));
        voteBlock.Should().Contain("return Results.Ok(response);");
    }

    [Fact]
    public void VoteTimingJitter_UsesFiveToFiftyMilliseconds()
    {
        var programText = ReadProgram();
        var jitterBlock = SliceBlock(
            programText,
            "static Task AddVoteTimingJitterAsync",
            "static async Task<(string? OldVote, int OldTotal");

        jitterBlock.Should().Contain("Random.Shared.Next(5, 51)");
        jitterBlock.Should().Contain("Task.Delay");
    }

    // ── Duplicate vote / unique constraint ─────────────────────────────────

    [Fact]
    public void VoteUpsert_UsesUniqueConstraintToPreventDuplicateVotes()
    {
        var programText = ReadProgram();
        var upsertBlock = SliceBlock(
            programText,
            "static async Task UpsertVoteAsync",
            "static async Task UpdateVoteCountersAsync");

        // DB-level uniqueness prevents two different vote rows per (post, device)
        upsertBlock.Should().Contain("ON CONFLICT (post_id, device_id)");
        // Conflict resolution: update the existing row rather than silently ignoring
        upsertBlock.Should().Contain("DO UPDATE SET");
    }

    [Fact]
    public void VoteUpsert_ResetsSuppressionBeforeReevaluationButKeepsUniqueConstraint()
    {
        var programText = ReadProgram();
        var upsertBlock = SliceBlock(
            programText,
            "static async Task UpsertVoteAsync",
            "static async Task UpdateVoteCountersAsync");

        upsertBlock.Should().Contain("ON CONFLICT (post_id, device_id)");
        upsertBlock.Should().Contain("is_suppressed = FALSE");
        upsertBlock.Should().Contain("suppression_reason = NULL");
        upsertBlock.Should().Contain("suppressed_at = NULL");
    }

    // ── Suspicious device quarantine ───────────────────────────────────────

    [Fact]
    public void VoteEndpoint_QuarantinedVotesExcludedFromTrendPropagationButCountsPreserved()
    {
        var programText = ReadProgram();
        var voteBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        // Visible counters (hakli/haksiz) are always updated — quarantine must not hide votes
        voteBlock.Should().Contain("UpdateVoteCountersReturningAsync(");
        // Trend-score propagation is skipped for suspicious votes
        voteBlock.Should().Contain("!trustDecision.ShouldQuarantineVote");
        voteBlock.Should().Contain("MarkPostDirtyAsync(");
    }

    [Fact]
    public void TrendScoreCalculation_ExcludesQuarantinedVotes()
    {
        var programText = ReadProgram();
        // The TrendScoreUpdater SQL must filter out quarantined votes so they
        // don't artificially inflate trend scores.
        programText.Should().Contain("v.is_quarantined = FALSE",
            "trend score SQL must exclude quarantined (suspicious device) votes");
    }

    [Fact]
    public void VoteCounters_CountOnlyUnsuppressedVotes()
    {
        var programText = ReadProgram();
        var counterBlock = SliceBlock(
            programText,
            "static async Task<(int Hakli, int Haksiz)> UpdateVoteCountersReturningAsync",
            "static async Task UpsertVoteAsync");

        counterBlock.Should().Contain("v.vote_type = 'hakli'");
        counterBlock.Should().Contain("v.vote_type = 'haksiz'");
        counterBlock.Should().Contain("v.is_suppressed = FALSE",
            "normal votes count, suppressed brigade votes do not");
        counterBlock.Should().NotContain("vote_count_hakli + @hakliDelta",
            "counter updates must be recomputed from DB state after suppression");
    }

    [Fact]
    public void TrendScoreCalculation_ExcludesSuppressedVotes()
    {
        var programText = ReadProgram();

        programText.Should().Contain("v.is_suppressed = FALSE",
            "suppressed brigade votes must not inflate ranking or inline trend_score");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string ReadProgram() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }
}
