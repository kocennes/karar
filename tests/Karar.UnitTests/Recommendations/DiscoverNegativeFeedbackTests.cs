using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

/// <summary>
/// Contract tests for Discover feed negative feedback signals.
/// All tests are source-file assertions — no Redis/DB mock needed.
/// </summary>
public sealed class DiscoverNegativeFeedbackTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly string RedisServiceText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "RedisService.cs");

    // Slice just the discover/feed endpoint block for targeted assertions
    private static string DiscoverFeedBlock => SliceBlock(
        ProgramText,
        "app.MapGet(\"/api/v1/posts/discover/feed\"",
        "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

    // Slice just the discover/events endpoint block
    private static string DiscoverEventsBlock => SliceBlock(
        ProgramText,
        "app.MapPost(\"/api/v1/posts/discover/events\"",
        "app.MapGet(\"/api/v1/search\"");

    // ── Discover feed — not-interested suppression ──────────────────────────

    [Fact]
    public void DiscoverFeed_ReadsNotInterestedListFromRedis()
    {
        DiscoverFeedBlock.Should().Contain("GetNotInterestedPostsAsync",
            because: "discover/feed must exclude posts the user marked not-interested");
    }

    [Fact]
    public void DiscoverFeed_FiltersSuppressedPostsViaSqlParameter()
    {
        DiscoverFeedBlock.Should().Contain("suppressedIds",
            because: "suppressed post IDs must be passed as a SQL parameter to exclude them");
        DiscoverFeedBlock.Should().Contain("ALL(@suppressedIds)",
            because: "SQL must use != ALL(@suppressedIds) to filter in a single pass");
    }

    [Fact]
    public void DiscoverFeed_ReadsSkipSuppressedListFromRedis()
    {
        DiscoverFeedBlock.Should().Contain("GetSkipSuppressedPostsAsync",
            because: "discover/feed must also suppress recently-skipped posts (24-hour soft signal)");
    }

    [Fact]
    public void DiscoverFeed_MergesNotInterestedAndSkipSuppressed()
    {
        DiscoverFeedBlock.Should().Contain("UnionWith",
            because: "not-interested and skip-suppressed sets must be merged before the SQL filter");
    }

    // ── Discover events — not_interested writes to Redis ────────────────────

    [Fact]
    public void DiscoverEvents_NotInterestedWritesToRedis()
    {
        DiscoverEventsBlock.Should().Contain("MarkNotInterestedAsync",
            because: "not_interested event must persist the signal to Redis so feed can filter it");
    }

    // ── Discover events — skip soft signal ──────────────────────────────────

    [Fact]
    public void DiscoverEvents_SkipWritesSoftNegativeSignalToRedis()
    {
        DiscoverEventsBlock.Should().Contain("AddSkipSuppressionAsync",
            because: "skip event must save a short-TTL soft signal so recently-skipped posts are suppressed");
    }

    [Fact]
    public void DiscoverEvents_SkipDoesNotBlockDeviceOrUser()
    {
        // The skip branch must NOT touch banned devices, quarantine, or user blocks.
        // It must only call AddSkipSuppressionAsync — nothing destructive.
        var skipBranch = SliceBlock(
            DiscoverEventsBlock,
            "request.EventType == \"skip\"",
            "return Results.NoContent()");

        skipBranch.Should().Contain("AddSkipSuppressionAsync");
        skipBranch.Should().NotContain("is_banned",
            because: "skip must not set the is_banned flag — it is a soft signal only");
        skipBranch.Should().NotContain("is_quarantined",
            because: "skip must not quarantine votes — it only suppresses feed visibility");
        skipBranch.Should().NotContain("BlockUser",
            because: "skip must not block the user/device");
    }

    // ── RedisService — skip suppression contract ─────────────────────────────

    [Fact]
    public void RedisService_HasAddSkipSuppressionMethod()
    {
        RedisServiceText.Should().Contain("AddSkipSuppressionAsync",
            because: "RedisService must expose a method to record a skip signal");
    }

    [Fact]
    public void RedisService_HasGetSkipSuppressedMethod()
    {
        RedisServiceText.Should().Contain("GetSkipSuppressedPostsAsync",
            because: "RedisService must expose a method to retrieve skip-suppressed post IDs");
    }

    [Fact]
    public void RedisService_SkipSuppressionUsesShortTtl()
    {
        // The window constant must be shorter than the 30-day not-interested window.
        // We verify it references "hours" — not "days" — in the TTL vicinity.
        var skipBlock = SliceBlock(
            RedisServiceText,
            "SkipSuppressWindow",
            "AddSkipSuppressionAsync");

        skipBlock.Should().Contain("Hours",
            because: "skip suppression must use an hour-scale TTL (soft signal), not a day-scale one");
        skipBlock.Should().NotContain("FromDays",
            because: "skip TTL must be shorter than the 30-day not-interested window");
    }

    [Fact]
    public void RedisService_NotInterestedWindowIsLongerThanSkipWindow()
    {
        // not_interested = 30 days; skip = 24 hours — verify both constants are present
        // and not_interested uses FromDays while skip uses FromHours.
        RedisServiceText.Should().Contain("NotInterestedWindow = TimeSpan.FromDays");
        RedisServiceText.Should().Contain("SkipSuppressWindow = TimeSpan.FromHours");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }
}
