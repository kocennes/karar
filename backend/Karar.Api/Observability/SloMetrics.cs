using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Karar.Api.Observability;

public sealed class SloMetrics(IOptions<SloOptions> options)
{
    private const int MaxSamples = 20_000;
    private readonly ConcurrentQueue<ApiSample> _api = new();
    private readonly ConcurrentQueue<OutcomeSample> _notifications = new();
    private readonly ConcurrentQueue<LatencySample> _moderation = new();
    private long _apiCount;
    private long _notificationCount;
    private long _moderationCount;

    public void RecordApiRequest(string route, string method, int statusCode, double elapsedMs)
    {
        var now = DateTimeOffset.UtcNow;
        Enqueue(_api, new ApiSample(now, route, method, statusCode, elapsedMs), ref _apiCount);
        KararTelemetry.RecordApiRequest(route, method, statusCode, elapsedMs);
    }

    public void RecordNotificationDelivery(string outcome, string type)
    {
        var success = outcome.Equals("sent", StringComparison.OrdinalIgnoreCase) ||
                      outcome.Equals("success", StringComparison.OrdinalIgnoreCase);
        Enqueue(_notifications, new OutcomeSample(DateTimeOffset.UtcNow, success), ref _notificationCount);
        KararTelemetry.RecordNotificationDelivery(success ? "success" : "failure", type);
    }

    public void RecordModeration(TimeSpan elapsed, bool rejected)
    {
        Enqueue(_moderation, new LatencySample(DateTimeOffset.UtcNow, elapsed.TotalMilliseconds), ref _moderationCount);
        KararTelemetry.RecordModeration(elapsed.TotalMilliseconds, rejected ? "rejected" : "accepted");
    }

    public SloGateSnapshot GetSnapshot()
    {
        var currentOptions = options.Value;
        var cutoff = DateTimeOffset.UtcNow.Subtract(currentOptions.Window);
        Trim(_api, cutoff, ref _apiCount);
        Trim(_notifications, cutoff, ref _notificationCount);
        Trim(_moderation, cutoff, ref _moderationCount);

        var apiSamples = _api.Where(s => s.Timestamp >= cutoff).ToArray();
        var notificationSamples = _notifications.Where(s => s.Timestamp >= cutoff).ToArray();
        var moderationSamples = _moderation.Where(s => s.Timestamp >= cutoff).ToArray();
        var feedSamples = apiSamples.Where(s => KararTelemetry.IsFeedRoute(s.Method, s.Route)).ToArray();
        var voteSamples = apiSamples.Where(s => KararTelemetry.IsVoteRoute(s.Method, s.Route)).ToArray();

        var apiAvailability = Percent(apiSamples.Count(s => s.StatusCode < 500), apiSamples.Length);
        var voteSuccess = Percent(voteSamples.Count(s => s.StatusCode is >= 200 and < 300), voteSamples.Length);
        var notificationDelivery = Percent(notificationSamples.Count(s => s.Success), notificationSamples.Length);

        var checks = new[]
        {
            BuildPercentageCheck("api_availability", apiAvailability, currentOptions.ApiAvailabilityTarget, apiSamples.Length, currentOptions.MinimumEventsForGate, higherIsBetter: true),
            BuildLatencyCheck("api_p95_latency", Percentile(apiSamples.Select(s => s.ElapsedMs), 95), currentOptions.ApiP95LatencyMs, apiSamples.Length, currentOptions.MinimumEventsForGate),
            BuildLatencyCheck("feed_p95_latency", Percentile(feedSamples.Select(s => s.ElapsedMs), 95), currentOptions.FeedP95LatencyMs, feedSamples.Length, currentOptions.MinimumEventsForGate),
            BuildPercentageCheck("vote_success", voteSuccess, currentOptions.VoteSuccessTarget, voteSamples.Length, currentOptions.MinimumEventsForGate, higherIsBetter: true),
            BuildPercentageCheck("notification_delivery", notificationDelivery, currentOptions.NotificationDeliveryTarget, notificationSamples.Length, currentOptions.MinimumEventsForGate, higherIsBetter: true),
            BuildLatencyCheck("moderation_sla_p95", Percentile(moderationSamples.Select(s => s.ElapsedMs), 95), currentOptions.ModerationP95LatencyMs, moderationSamples.Length, currentOptions.MinimumEventsForGate)
        };

        var burnRates = currentOptions.BurnRatePolicies
            .Select(policy => BuildBurnRate(policy, apiSamples, currentOptions.ApiAvailabilityTarget))
            .ToArray();

        var status = checks.Any(c => c.Status == "fail") || burnRates.Any(b => b.Status == "alert")
            ? "fail"
            : checks.Any(c => c.Status == "insufficient_data")
                ? "insufficient_data"
                : "pass";

        return new SloGateSnapshot(
            status,
            currentOptions.Window,
            currentOptions.MinimumEventsForGate,
            checks,
            burnRates);
    }

    private static SloCheck BuildPercentageCheck(
        string name,
        double? value,
        double target,
        int sampleCount,
        int minimumEvents,
        bool higherIsBetter)
    {
        var status = sampleCount < minimumEvents
            ? "insufficient_data"
            : higherIsBetter
                ? value >= target ? "pass" : "fail"
                : value <= target ? "pass" : "fail";

        return new SloCheck(name, status, value, target, "%", sampleCount);
    }

    private static SloCheck BuildLatencyCheck(
        string name,
        double? value,
        double target,
        int sampleCount,
        int minimumEvents)
    {
        var status = sampleCount < minimumEvents
            ? "insufficient_data"
            : value <= target ? "pass" : "fail";

        return new SloCheck(name, status, value, target, "ms", sampleCount);
    }

    private static BurnRateSnapshot BuildBurnRate(BurnRatePolicy policy, ApiSample[] samples, double availabilityTarget)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(policy.Window);
        var windowSamples = samples.Where(s => s.Timestamp >= cutoff).ToArray();
        var errorBudget = Math.Max(0.000001, 100.0 - availabilityTarget);
        var errorRate = Percent(windowSamples.Count(s => s.StatusCode >= 500), windowSamples.Length) ?? 0;
        var burnRate = errorRate / errorBudget;

        return new BurnRateSnapshot(
            policy.Name,
            policy.Window,
            policy.Threshold,
            policy.Severity,
            burnRate,
            burnRate >= policy.Threshold ? "alert" : "ok",
            windowSamples.Length);
    }

    private static void Enqueue<T>(ConcurrentQueue<T> queue, T sample, ref long count)
    {
        queue.Enqueue(sample);
        if (Interlocked.Increment(ref count) <= MaxSamples)
            return;

        while (Interlocked.Read(ref count) > MaxSamples && queue.TryDequeue(out _))
            Interlocked.Decrement(ref count);
    }

    private static void Trim<T>(ConcurrentQueue<T> queue, DateTimeOffset cutoff, ref long count)
        where T : ITimedSample
    {
        while (queue.TryPeek(out var sample) && sample.Timestamp < cutoff && queue.TryDequeue(out _))
            Interlocked.Decrement(ref count);
    }

    private static double? Percent(int numerator, int denominator) =>
        denominator == 0 ? null : Math.Round(numerator * 100.0 / denominator, 4);

    private static double? Percentile(IEnumerable<double> values, int percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
            return null;

        var rank = (percentile / 100.0) * (sorted.Length - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
            return Math.Round(sorted[lower], 2);

        var value = sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
        return Math.Round(value, 2);
    }

    private interface ITimedSample
    {
        DateTimeOffset Timestamp { get; }
    }

    private sealed record ApiSample(
        DateTimeOffset Timestamp,
        string Route,
        string Method,
        int StatusCode,
        double ElapsedMs) : ITimedSample;

    private sealed record OutcomeSample(DateTimeOffset Timestamp, bool Success) : ITimedSample;

    private sealed record LatencySample(DateTimeOffset Timestamp, double ElapsedMs) : ITimedSample;
}

public sealed record SloGateSnapshot(
    string Status,
    TimeSpan Window,
    int MinimumEventsForGate,
    IReadOnlyCollection<SloCheck> Checks,
    IReadOnlyCollection<BurnRateSnapshot> BurnRatePolicies);

public sealed record SloCheck(
    string Name,
    string Status,
    double? Value,
    double Target,
    string Unit,
    int SampleCount);

public sealed record BurnRateSnapshot(
    string Name,
    TimeSpan Window,
    double Threshold,
    string Severity,
    double BurnRate,
    string Status,
    int SampleCount);
