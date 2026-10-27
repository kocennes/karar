using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Regression: a brigade-suppressed vote used to be un-suppressed by any later vote change
// (UpsertVoteAsync's ON CONFLICT reset is_suppressed) or by DELETE + re-vote. The guard only
// looks at votes created in the last 10 minutes, so after the window it never re-suppressed
// the laundered cluster and the published verdict moved. Suppression must be sticky per
// (post, device).
public sealed class VoteSuppressionStickinessTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void VoteUpsert_ChangingVoteDoesNotClearSuppression()
    {
        var upsert = Slice(ProgramText, "static async Task UpsertVoteAsync", "static async Task UpdateVoteCountersAsync");
        var onConflict = Slice(upsert, "DO UPDATE SET", "\"\"\"");

        onConflict.Should().NotContain("is_suppressed = FALSE",
            "flipping the vote after the guard window must not un-suppress a brigade vote");
        onConflict.Should().NotContain("suppression_reason = NULL");
        onConflict.Should().NotContain("suppressed_at = NULL");
        onConflict.Should().Contain("is_quarantined = @isQuarantined OR votes.is_suppressed",
            "a suppressed vote stays quarantined as well");
    }

    [Fact]
    public void VoteDelete_RemembersSuppressionForLaterRevote()
    {
        var deleteBlock = Slice(
            ProgramText,
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/events\"");

        deleteBlock.Should().Contain("INSERT INTO vote_suppression_history",
            "DELETE then re-vote must not launder a suppressed vote into a fresh unsuppressed row");
        deleteBlock.Should().Contain("WHERE is_suppressed");
    }

    [Fact]
    public void VoteUpsert_ReappliesRememberedSuppressionOnFreshInsert()
    {
        var upsert = Slice(ProgramText, "static async Task UpsertVoteAsync", "static async Task UpdateVoteCountersAsync");

        upsert.Should().Contain("FROM vote_suppression_history h",
            "a re-inserted vote from a previously suppressed device must start suppressed");
        upsert.Should().Contain("is_suppressed = TRUE");
    }

    [Fact]
    public void Migration_CreatesVoteSuppressionHistory()
    {
        TestRepoPaths.TryReadText(out var migration, "backend", "migrations", "V111__vote_suppression_history.sql")
            .Should().BeTrue("suppression memory needs its own table");

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS vote_suppression_history");
        migration.Should().Contain("PRIMARY KEY (post_id, device_id)");
        migration.Should().Contain("REFERENCES posts(id) ON DELETE CASCADE");
        migration.Should().Contain("REFERENCES devices(id) ON DELETE CASCADE",
            "device erasure must not leave suppression rows behind");
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
