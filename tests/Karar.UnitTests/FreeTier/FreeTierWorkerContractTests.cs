using System.Text.RegularExpressions;
using FluentAssertions;

namespace Karar.UnitTests.FreeTier;

/// Source contract: every timer worker that touches Postgres consults the free-tier idle gate.
/// Gated workers skip the whole cycle while idle; rare (hourly/daily) jobs and Redis-signalled
/// jobs keep running but leave a Debug trace, so a Neon wake-up can always be attributed.
public sealed class FreeTierWorkerContractTests
{
    private static readonly string[] AllWorkers =
    [
        "ImageModerationWorker.cs",
        "NotificationDispatcher.cs",
        "TrendScoreUpdater.cs",
        "PostDistributionJob.cs",
        "BrigadeCoordinatedDetectorJob.cs",
        "ViralNotificationJob.cs",
        "CommentNotificationBatcher.cs",
        "DeferredNotificationFlushJob.cs",
        "VerdictReminderJob.cs",
        "AuditLogExportJob.cs",
        "PoliticalNarrativeClusterJob.cs",
        "DataRetentionService.cs",
    ];

    // Idle => the whole DB-touching cycle is skipped with a single Debug log.
    private static readonly string[] GatedWorkers =
    [
        "ImageModerationWorker.cs",
        "NotificationDispatcher.cs",
        "TrendScoreUpdater.cs",
        "PostDistributionJob.cs",
        "BrigadeCoordinatedDetectorJob.cs",
        "ViralNotificationJob.cs",
    ];

    // Keep their schedule (rare) or have a Redis-only signal for pending work; never skipped.
    private static readonly string[] UngatedWorkers =
    [
        "CommentNotificationBatcher.cs",
        "DeferredNotificationFlushJob.cs",
        "VerdictReminderJob.cs",
        "AuditLogExportJob.cs",
        "PoliticalNarrativeClusterJob.cs",
        "DataRetentionService.cs",
    ];

    private static readonly Regex IdleSkipBlock = new(
        @"if \(activityTracker\.IsIdle\([^)]*\)?\)\)\s*\{\s*logger\.LogDebug\([^;]*\);\s*return;\s*\}",
        RegexOptions.Compiled);

    public static TheoryData<string> AllWorkerFiles => new(AllWorkers);
    public static TheoryData<string> GatedWorkerFiles => new(GatedWorkers);
    public static TheoryData<string> UngatedWorkerFiles => new(UngatedWorkers);

    private static string Worker(string file) =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", file);

    [Fact]
    public void WorkerLists_CoverEveryHostedTimerWorker()
    {
        GatedWorkers.Concat(UngatedWorkers).Should().BeEquivalentTo(AllWorkers);
    }

    [Theory]
    [MemberData(nameof(AllWorkerFiles))]
    public void EveryTimerWorker_ReferencesTheIdleGate(string file)
    {
        var source = Worker(file);

        source.Should().Contain("ActivityTracker activityTracker", "the gate is injected through the constructor");
        source.Should().Contain("activityTracker.IsIdle()", "the gate is consulted before DB work");
        source.Should().Contain("BackgroundService", "only hosted timer workers are in scope");
    }

    [Theory]
    [MemberData(nameof(GatedWorkerFiles))]
    public void GatedWorker_SkipsCycleWithDebugLog_WhenIdle(string file)
    {
        var source = Worker(file);

        IdleSkipBlock.IsMatch(source).Should().BeTrue(
            "{0} must skip the cycle (Debug log + return) while the API is idle", file);
        source.Should().NotContain("LogInformation(\"" + Path.GetFileNameWithoutExtension(file) + ": API idle",
            "skipped cycles are logged at Debug, not Information");
    }

    [Theory]
    [MemberData(nameof(UngatedWorkerFiles))]
    public void UngatedWorker_KeepsRunning_WhenIdle(string file)
    {
        var source = Worker(file);

        IdleSkipBlock.IsMatch(source).Should().BeFalse(
            "{0} is rare or Redis-signalled and must keep its schedule regardless of idleness", file);
    }

    [Fact]
    public void TrendScoreUpdater_DirtyFastPath_RunsRegardlessOfIdleness()
    {
        var source = Worker("TrendScoreUpdater.cs");
        var fastPathStart = source.IndexOf("UpdateDirtyTrendScoresAsync(CancellationToken ct)", StringComparison.Ordinal);
        var slowPathStart = source.IndexOf("UpdateAllActiveTrendScoresAsync(CancellationToken ct)", StringComparison.Ordinal);
        fastPathStart.Should().BePositive();
        slowPathStart.Should().BeGreaterThan(fastPathStart);

        var fastPath = source[fastPathStart..slowPathStart];
        fastPath.Should().Contain("GetDirtyPostsAsync", "the Redis posts:dirty set is the pending-work signal");
        fastPath.Should().NotContain("IsIdle", "skip only when posts:dirty is empty, which the fast path already does");

        var slowPath = source[slowPathStart..];
        IdleSkipBlock.IsMatch(slowPath).Should().BeTrue("the full age-decay pass is skipped while idle");
    }

    [Theory]
    [InlineData("ViralNotificationJob.cs", "activityTracker.IsIdle(VelocityWindow)")]
    [InlineData("BrigadeCoordinatedDetectorJob.cs", "activityTracker.IsIdle(TimeSpan.FromMinutes(VoteWindowMinutes))")]
    public void LookbackWindowJobs_GateOnTheirOwnWindow_NotOnIdleAfter(string file, string gate)
    {
        // Gating on IdleAfter (default 10 min) is wrong for a job whose query looks back further than that:
        // 20 votes between 14:00 and 14:05, silence afterwards, viral tick at 14:20 => skipped as idle,
        // next tick at 14:50 => the 30-min velocity window is empty, the viral push is lost for good.
        // "No request within the job's own window" is the exact condition for "nothing to find".
        Worker(file).Should().Contain(gate);
    }

    [Theory]
    [InlineData("CommentNotificationBatcher.cs")]
    [InlineData("DeferredNotificationFlushJob.cs")]
    [InlineData("VerdictReminderJob.cs")]
    public void OutboxProducersThatRunWhileIdle_WakeTheDispatcher(string file)
    {
        // These insert into the notifications outbox even while idle; without this the
        // (gated) NotificationDispatcher would hold the push until the next user request.
        Worker(file).Should().Contain("activityTracker.MarkPendingWork()");
    }

    [Theory]
    [InlineData("ImageModerationWorker.cs", "TimeSpan.FromSeconds(30)")]
    [InlineData("NotificationDispatcher.cs", "Interval = TimeSpan.FromSeconds(30)")]
    [InlineData("TrendScoreUpdater.cs", "Interval = TimeSpan.FromMinutes(2)")]
    [InlineData("PostDistributionJob.cs", "CheckIntervalSeconds = 120")]
    [InlineData("BrigadeCoordinatedDetectorJob.cs", "Interval = TimeSpan.FromMinutes(10)")]
    [InlineData("ViralNotificationJob.cs", "Interval = TimeSpan.FromMinutes(30)")]
    [InlineData("CommentNotificationBatcher.cs", "FlushInterval = TimeSpan.FromMinutes(1)")]
    [InlineData("DeferredNotificationFlushJob.cs", "FlushTimeUtc = new(5, 0, 0)")]
    [InlineData("VerdictReminderJob.cs", "Interval = TimeSpan.FromHours(1)")]
    [InlineData("AuditLogExportJob.cs", "CheckInterval = TimeSpan.FromHours(1)")]
    [InlineData("PoliticalNarrativeClusterJob.cs", "Interval = TimeSpan.FromHours(6)")]
    [InlineData("DataRetentionService.cs", "Interval = TimeSpan.FromDays(1)")]
    public void WorkerIntervals_AreUnchanged(string file, string intervalSnippet)
    {
        Worker(file).Should().Contain(intervalSnippet, "idle-awareness must not change worker cadence");
    }

    [Fact]
    public void Program_RegistersGateAndMiddleware_BeforeRequestLogging()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("builder.Services.AddSingleton<ActivityTracker>();");

        var tracking = program.IndexOf("app.UseMiddleware<ActivityTrackingMiddleware>();", StringComparison.Ordinal);
        var logging = program.IndexOf("app.UseMiddleware<RedactedRequestLoggingMiddleware>();", StringComparison.Ordinal);
        tracking.Should().BePositive();
        tracking.Should().BeLessThan(logging, "activity is marked as early and cheaply as possible");

        program.Should().Contain("static bool IsCrawler(string userAgent) => CrawlerDetector.IsCrawler(userAgent);",
            "the OG shell and the middleware must share one crawler definition");
    }
}
