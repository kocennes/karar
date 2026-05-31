namespace Karar.Api.Observability;

public sealed class SloOptions
{
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(30);
    public double ApiAvailabilityTarget { get; set; } = 99.9;
    public double ApiP95LatencyMs { get; set; } = 500;
    public double FeedP95LatencyMs { get; set; } = 350;
    public double VoteSuccessTarget { get; set; } = 99.5;
    public double NotificationDeliveryTarget { get; set; } = 99.0;
    public double ModerationP95LatencyMs { get; set; } = 60_000;
    public int MinimumEventsForGate { get; set; } = 20;
    public List<BurnRatePolicy> BurnRatePolicies { get; set; } =
    [
        new("critical-fast", TimeSpan.FromMinutes(5), 14.4, "page"),
        new("critical-slow", TimeSpan.FromHours(1), 6.0, "page"),
        new("warning-fast", TimeSpan.FromHours(6), 3.0, "slack"),
        new("warning-slow", TimeSpan.FromDays(3), 1.0, "slack")
    ];

    /// <summary>HTTP POST endpoint for "page" severity alerts (e.g. PagerDuty, generic webhook).</summary>
    public string? PageWebhookUrl { get; set; }

    /// <summary>HTTP POST endpoint for "slack" severity alerts (Slack incoming webhook).</summary>
    public string? SlackWebhookUrl { get; set; }

    /// <summary>Minimum time between repeated "page" alerts for the same policy.</summary>
    public TimeSpan PageAlertCooldown { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Minimum time between repeated "slack" alerts for the same policy.</summary>
    public TimeSpan SlackAlertCooldown { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How often BurnRateAlertWorker evaluates burn rates.</summary>
    public TimeSpan AlertCheckInterval { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed record BurnRatePolicy(
    string Name,
    TimeSpan Window,
    double Threshold,
    string Severity);
