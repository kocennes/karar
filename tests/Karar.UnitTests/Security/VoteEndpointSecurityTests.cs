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

    // [F10] A brigade (>5 devices from one /24 or fingerprint cluster within 10 min) is suppressed by
    // VoteBrigadeGuard. The guard window is keyed on votes.created_at, which the upsert never changes,
    // so a re-vote after the window is not re-evaluated. If the upsert cleared is_suppressed, every
    // brigade device could flip haksiz -> hakli after 10 minutes and be counted again.
    [Fact]
    public void VoteUpsert_KeepsBrigadeSuppressionStickyAndKeepsUniqueConstraint()
    {
        var programText = ReadProgram();
        var upsertBlock = SliceBlock(
            programText,
            "static async Task UpsertVoteAsync",
            "static async Task UpdateVoteCountersAsync");
        var conflictUpdate = SliceBlock(upsertBlock, "DO UPDATE SET", "\"\"\"");

        upsertBlock.Should().Contain("ON CONFLICT (post_id, device_id)");
        conflictUpdate.Should().NotContain("is_suppressed =",
            "a client re-vote must not clear server-side brigade suppression");
        conflictUpdate.Should().NotContain("suppression_reason =",
            "the suppression audit reason must survive a re-vote");
        conflictUpdate.Should().NotContain("suppressed_at =",
            "the suppression timestamp must survive a re-vote");
        conflictUpdate.Should().Contain("is_quarantined = @isQuarantined OR votes.is_suppressed",
            "suppressed votes must also stay out of legacy is_quarantined-only ranking paths");
    }

    [Fact]
    public void VoteEndpoint_TreatsStoredSuppressionAsSuppressedForSideEffects()
    {
        var programText = ReadProgram();
        var contextBlock = SliceBlock(
            programText,
            "static async Task<(string? OldVote, int OldTotal",
            "static async Task<(int Hakli, int Haksiz)> UpdateVoteCountersReturningAsync");
        var voteBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        contextBlock.Should().Contain("COALESCE(v.is_suppressed, FALSE)",
            "the vote context must expose whether the device's stored vote is suppressed");
        voteBlock.Should().Contain("var voteIsSuppressed = brigadeResult.Detected || existingVoteSuppressed;",
            "a re-vote of a suppressed vote must not feed affinity or city trending after the guard window");
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
