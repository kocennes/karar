using FluentAssertions;
using Karar.Api.Observability;
using Microsoft.Extensions.Options;

namespace Karar.UnitTests.Observability;

public sealed class SloMetricsTests
{
    [Fact]
    public void SloGate_Passes_WhenMetricsMeetTargets()
    {
        var metrics = CreateMetrics(minimumEvents: 2);

        metrics.RecordApiRequest("/api/v1/posts", "GET", 200, 120);
        metrics.RecordApiRequest("/api/v1/posts", "GET", 200, 180);
        metrics.RecordApiRequest("/api/v1/posts/{id:guid}/vote", "POST", 200, 80);
        metrics.RecordApiRequest("/api/v1/posts/{id:guid}/vote", "POST", 200, 90);
        metrics.RecordNotificationDelivery("sent", "comment_on_post");
        metrics.RecordNotificationDelivery("sent", "reply_on_comment");
        metrics.RecordModeration(TimeSpan.FromMilliseconds(20), rejected: false);
        metrics.RecordModeration(TimeSpan.FromMilliseconds(30), rejected: false);

        var snapshot = metrics.GetSnapshot();

        snapshot.Status.Should().Be("pass");
        snapshot.Checks.Should().OnlyContain(c => c.Status == "pass");
    }

    [Fact]
    public void SloGate_Fails_WhenErrorBudgetBurnRateBreachesPolicy()
    {
        var metrics = CreateMetrics(minimumEvents: 2);

        metrics.RecordApiRequest("/api/v1/posts", "GET", 500, 100);
        metrics.RecordApiRequest("/api/v1/posts", "GET", 500, 100);
        metrics.RecordNotificationDelivery("sent", "comment_on_post");
        metrics.RecordNotificationDelivery("sent", "comment_on_post");
        metrics.RecordModeration(TimeSpan.FromMilliseconds(20), rejected: false);
        metrics.RecordModeration(TimeSpan.FromMilliseconds(30), rejected: false);

        var snapshot = metrics.GetSnapshot();

        snapshot.Status.Should().Be("fail");
        snapshot.BurnRatePolicies.Should().Contain(b => b.Status == "alert");
        snapshot.Checks.Single(c => c.Name == "api_availability").Status.Should().Be("fail");
    }

    [Fact]
    public void TelemetryConfig_DefaultsToDisabled_WhenNoEndpointIsConfigured()
    {
        var appSettings = TestRepoPaths.ReadText("backend", "Karar.Api", "appsettings.json");

        appSettings.Should().Contain("\"Observability\"");
        appSettings.Should().Contain("\"Enabled\": false");
        appSettings.Should().Contain("\"OtlpEndpoint\": \"\"");
    }

    private static SloMetrics CreateMetrics(int minimumEvents)
    {
        var options = Options.Create(new SloOptions
        {
            Window = TimeSpan.FromMinutes(30),
            MinimumEventsForGate = minimumEvents,
            ApiAvailabilityTarget = 99,
            ApiP95LatencyMs = 500,
            FeedP95LatencyMs = 350,
            VoteSuccessTarget = 99,
            NotificationDeliveryTarget = 99,
            ModerationP95LatencyMs = 1000
        });

        return new SloMetrics(options);
    }
}
