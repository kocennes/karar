using FluentAssertions;
using Karar.Api.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Karar.UnitTests.FreeTier;

public sealed class ActivityTrackerTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(10);

    [Fact]
    public void FreshTracker_IsNotIdle_BeforeThresholdElapses()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ActivityTracker(enabled: true, idleAfter: Threshold, clock);

        clock.Advance(Threshold - TimeSpan.FromSeconds(1));

        tracker.IsIdle(Threshold).Should().BeFalse("startup counts as activity until the threshold passes");
    }

    [Fact]
    public void IsIdle_ReturnsTrue_OnceThresholdElapsesWithoutRequests()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ActivityTracker(enabled: true, idleAfter: Threshold, clock);

        clock.Advance(Threshold);

        tracker.IsIdle(Threshold).Should().BeTrue();
        tracker.IsIdle().Should().BeTrue("the parameterless overload uses the configured threshold");
    }

    [Fact]
    public void MarkRequest_ResetsIdleTimer()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ActivityTracker(enabled: true, idleAfter: Threshold, clock);
        clock.Advance(TimeSpan.FromHours(3));
        tracker.IsIdle().Should().BeTrue();

        tracker.MarkRequest();

        tracker.IsIdle().Should().BeFalse("a request arriving ends idleness immediately");
        tracker.LastUserRequestUtc.Should().Be(clock.GetUtcNow().UtcDateTime);

        clock.Advance(Threshold - TimeSpan.FromSeconds(1));
        tracker.IsIdle().Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        tracker.IsIdle().Should().BeTrue();
    }

    [Fact]
    public void MarkPendingWork_EndsIdleness_WithoutCountingAsUserRequest()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ActivityTracker(enabled: true, idleAfter: Threshold, clock);
        var startedAt = clock.GetUtcNow().UtcDateTime;
        clock.Advance(TimeSpan.FromHours(1));
        tracker.IsIdle().Should().BeTrue();

        tracker.MarkPendingWork();

        tracker.IsIdle().Should().BeFalse("a worker that queued DB work for another worker keeps the API active");
        tracker.LastUserRequestUtc.Should().Be(startedAt, "pending work is not a user request");
    }

    [Fact]
    public void IsIdle_IsAlwaysFalse_WhenDisabled()
    {
        var clock = new ManualTimeProvider();
        var tracker = new ActivityTracker(enabled: false, idleAfter: Threshold, clock);

        clock.Advance(TimeSpan.FromDays(30));

        tracker.Enabled.Should().BeFalse();
        tracker.IsIdle().Should().BeFalse();
        tracker.IsIdle(TimeSpan.Zero).Should().BeFalse();
    }

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", false)]
    [InlineData("Development", false)]
    [InlineData("Testing", false)]
    public void Configuration_EnabledDefaultsToProductionOnly(string environmentName, bool expectedEnabled)
    {
        var configuration = new ConfigurationBuilder().Build();

        var tracker = new ActivityTracker(configuration, new TestHostEnvironment(environmentName));

        tracker.Enabled.Should().Be(expectedEnabled);
        tracker.IdleAfter.Should().Be(TimeSpan.FromMinutes(ActivityTracker.DefaultIdleAfterMinutes));
        ActivityTracker.DefaultIdleAfterMinutes.Should().Be(10);
    }

    [Theory]
    [InlineData("Production", "false", false)]
    [InlineData("Development", "true", true)]
    public void Configuration_ExplicitEnabledOverridesEnvironmentDefault(
        string environmentName, string configured, bool expectedEnabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ActivityTracker.EnabledKey] = configured,
                [ActivityTracker.IdleAfterMinutesKey] = "25",
            })
            .Build();

        var tracker = new ActivityTracker(configuration, new TestHostEnvironment(environmentName));

        tracker.Enabled.Should().Be(expectedEnabled);
        tracker.IdleAfter.Should().Be(TimeSpan.FromMinutes(25));
    }

    [Fact]
    public void Configuration_UsesWellKnownKeys()
    {
        ActivityTracker.EnabledKey.Should().Be("FreeTier:Enabled");
        ActivityTracker.IdleAfterMinutesKey.Should().Be("FreeTier:IdleAfterMinutes");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    public void Configuration_NonPositiveIdleMinutes_FallBackToDefault(string configured)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ActivityTracker.IdleAfterMinutesKey] = configured,
            })
            .Build();

        var tracker = new ActivityTracker(configuration, new TestHostEnvironment("Production"));

        tracker.IdleAfter.Should().Be(TimeSpan.FromMinutes(ActivityTracker.DefaultIdleAfterMinutes));
    }

    internal sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Karar.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
