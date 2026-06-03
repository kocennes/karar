using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

/// <summary>
/// Discover feed diversity pass davranışını doğrular.
/// Hem kaynak-kod varlığı (source-code presence) hem de
/// sabit değer (constant) testleri içerir.
/// </summary>
public sealed class DiscoverDiversityPassTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly string RedisServiceText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "RedisService.cs");

    private static readonly string AffinityServiceText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "AffinityService.cs");

    private static string DiscoverFeedBlock => SliceBlock(
        ProgramText,
        "app.MapGet(\"/api/v1/posts/discover/feed\"",
        "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

    // ── Aynı yazar max 2 post (SQL window function) ───────────────────────────

    [Fact]
    public void DiscoverFeed_AuthorCap_SqlWindowUsesAuthorRank2()
    {
        DiscoverFeedBlock.Should().Contain("author_rank <= 2",
            because: "discover/feed SQL must cap same author/device to 2 posts via window function");
    }

    [Fact]
    public void DiscoverFeed_AuthorCap_WindowPartitionsByAuthorOrDevice()
    {
        DiscoverFeedBlock.Should().Contain("PARTITION BY COALESCE(p.user_id::text, p.device_id::text)",
            because: "author window function must partition by user_id falling back to device_id");
    }

    // ── Aynı kategori max 3 post (in-memory pass) ────────────────────────────

    [Fact]
    public void DiscoverFeed_CategoryCap_InMemoryChecksAt3()
    {
        DiscoverFeedBlock.Should().Contain("catCount >= 3",
            because: "in-memory diversity pass must skip posts once a category has 3 results");
    }

    [Fact]
    public void DiscoverFeed_CategoryCap_TracksDictionaryPerCategory()
    {
        DiscoverFeedBlock.Should().Contain("categoryCounts",
            because: "per-category count must be tracked in a dictionary during the diversity pass");
    }

    // ── Arka arkaya aynı kategori max 2 (streak kontrolü) ────────────────────

    [Fact]
    public void DiscoverFeed_ConsecutiveStreak_Limit2()
    {
        DiscoverFeedBlock.Should().Contain("streak > 2",
            because: "discover/feed must skip a post when the same category appears more than 2 times consecutively");
    }

    // ── UCB etiketleme ────────────────────────────────────────────────────────

    [Fact]
    public void DiscoverFeed_UcbPosts_LabeledAsUcbExplore()
    {
        DiscoverFeedBlock.Should().Contain("ucb_explore",
            because: "UCB Stage 1 exploration posts must carry RankingReason='ucb_explore'");
    }

    // ── Seen-post dedup (Redis) ───────────────────────────────────────────────

    [Fact]
    public void DiscoverFeed_SeenPostDedup_ReadsFromRedis()
    {
        DiscoverFeedBlock.Should().Contain("GetSeenDiscoverPostIdsAsync",
            because: "discover/feed must read previously seen post IDs from Redis to exclude them");
    }

    [Fact]
    public void DiscoverFeed_SeenPostDedup_WritesToRedisAfterServing()
    {
        DiscoverFeedBlock.Should().Contain("AddSeenDiscoverPostsAsync",
            because: "discover/feed must persist shown post IDs to Redis for future dedup");
    }

    [Fact]
    public void DiscoverFeed_ClientSeenPostIds_AcceptedAsQueryParam()
    {
        DiscoverFeedBlock.Should().Contain("seenPostIds",
            because: "endpoint must accept client-provided seen_post_ids (max 100) to filter same-session duplication");
    }

    [Fact]
    public void DiscoverFeed_ClientSeenPostIds_MergedIntoSuppressedSet()
    {
        DiscoverFeedBlock.Should().Contain("suppressedPostIds.Add(seenId)",
            because: "client-provided seen IDs must be merged into the suppressed set before the SQL query");
    }

    // ── Redis seen-post window sabitler ──────────────────────────────────────

    [Fact]
    public void RedisService_SeenDiscoverKey_Uses7DayWindow()
    {
        RedisServiceText.Should().Contain("SeenDiscoverWindow = TimeSpan.FromDays(7)",
            because: "seen-discover window must be 7 days to match the serendipity interaction window");
    }

    [Fact]
    public void RedisService_SeenDiscoverMaxSize_Is500()
    {
        RedisServiceText.Should().Contain("SeenDiscoverMaxSize = 500",
            because: "sliding window must be capped at 500 IDs to bound Redis memory usage");
    }

    [Fact]
    public void RedisService_SeenDiscoverKey_UsesNotifSeenPrefix()
    {
        RedisServiceText.Should().Contain("notif:seen:",
            because: "seen-post Redis key must use notif:seen:{deviceId} prefix as specified in the contract");
    }

    // ── UCB Redis caching ─────────────────────────────────────────────────────

    [Fact]
    public void RedisService_UcbCache_Uses1HourTtl()
    {
        RedisServiceText.Should().Contain("UcbScoreTtl = TimeSpan.FromHours(1)",
            because: "UCB score cache TTL must be 1 hour; longer would serve stale cold-start scores");
    }

    [Fact]
    public void RedisService_UcbCache_KeyPrefixIsUcb()
    {
        RedisServiceText.Should().Contain("ucb:",
            because: "UCB Redis key must use ucb:{postId} prefix");
    }

    // ── Device affinity (anonim kullanıcı kişiselleştirme) ───────────────────

    [Fact]
    public void AffinityService_HasDeviceVoteMethod()
    {
        AffinityServiceText.Should().Contain("RecordVoteByDeviceAsync",
            because: "AffinityService must support device-based vote affinity for anonymous users");
    }

    [Fact]
    public void AffinityService_HasDeviceCommentMethod()
    {
        AffinityServiceText.Should().Contain("RecordCommentByDeviceAsync",
            because: "AffinityService must support device-based comment affinity for anonymous users");
    }

    [Fact]
    public void AffinityService_HasDeviceDecayMethod()
    {
        AffinityServiceText.Should().Contain("ApplyWeeklyDeviceDecayAsync",
            because: "device affinity must decay weekly (×0.9) to prevent stale preference signals");
    }

    [Fact]
    public void AffinityService_DeviceIncrementUsesDeviceCategoryAffinityTable()
    {
        AffinityServiceText.Should().Contain("device_category_affinity",
            because: "device affinity must be stored in device_category_affinity, not user_category_affinity");
    }

    [Fact]
    public void MainFeed_DeviceAffinity_JoinsDeviceCategoryAffinityForAnonymous()
    {
        ProgramText.Should().Contain("device_category_affinity uca ON uca.device_id = @deviceId",
            because: "main feed must JOIN device_category_affinity for anonymous devices to apply affinity boost");
    }

    // ── Fresh slot (epsilon-greedy 20%) ──────────────────────────────────────

    [Fact]
    public void DiscoverFeed_FreshSlot_Targets20PercentOfMainSlots()
    {
        DiscoverFeedBlock.Should().Contain("mainSlots * 0.20",
            because: "discover/feed fresh slot target must be 20% of mainSlots per ranking-model.md §5 epsilon-greedy rule");
    }

    [Fact]
    public void DiscoverFeed_FreshSlot_EnforcesMinimumOf1()
    {
        DiscoverFeedBlock.Should().Contain("freshSlotTarget",
            because: "discover/feed must compute freshSlotTarget = max(1, ceil(mainSlots * 0.20))");
        DiscoverFeedBlock.Should().Contain("Math.Max(1,",
            because: "fresh slot count must never fall below 1 even for very small feeds");
    }

    [Fact]
    public void DiscoverFeed_FreshSlot_SwapsFromPoolWithoutDuplicates()
    {
        DiscoverFeedBlock.Should().Contain("freshDeficit",
            because: "endpoint must compute a deficit so only missing fresh slots are swapped in");
        DiscoverFeedBlock.Should().Contain("!usedIds.Contains(p.Id) && IsFreshPost(p)",
            because: "fresh candidates must be filtered through usedIds to prevent duplicate post injection");
    }

    [Fact]
    public void DiscoverFeed_FreshSlot_LabeledAsFresh()
    {
        DiscoverFeedBlock.Should().Contain("RankingReason = \"fresh\"",
            because: "fresh slot posts must carry RankingReason='fresh' so clients render the correct badge");
    }

    [Fact]
    public void DiscoverFeed_IsFreshPost_Uses2HourWindow()
    {
        DiscoverFeedBlock.Should().Contain("FromHours(2)",
            because: "IsFreshPost and RankingReasonFor must both use a 2-hour window per the unified fresh slot definition");
        DiscoverFeedBlock.Should().NotContain("FromHours(12)",
            because: "12-hour window is inconsistent with the 2-hour fresh slot rule in ranking-model.md §5");
    }

    // ── not_interested analytics event ────────────────────────────────────────

    [Fact]
    public void DiscoverEvents_NotInterested_LogsPostNotInterestedAnalytics()
    {
        var eventsBlock = SliceBlock(
            ProgramText,
            "request.EventType == \"not_interested\"",
            "request.EventType == \"skip\"");

        eventsBlock.Should().Contain("post_not_interested",
            because: "not_interested signal must also write a post_not_interested analytics event");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }
}
