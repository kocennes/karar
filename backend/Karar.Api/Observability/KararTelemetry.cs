using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Karar.Api.Observability;

public static class KararTelemetry
{
    public const string ServiceName = "karar-api";
    public const string ActivitySourceName = "Karar.Api";
    public const string MeterName = "Karar.Api";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> ApiRequests = Meter.CreateCounter<long>(
        "karar.api.requests",
        unit: "{request}",
        description: "API request count by route template and status class.");

    private static readonly Histogram<double> ApiLatency = Meter.CreateHistogram<double>(
        "karar.api.latency",
        unit: "ms",
        description: "API request latency in milliseconds.");

    private static readonly Histogram<double> FeedLatency = Meter.CreateHistogram<double>(
        "karar.feed.latency",
        unit: "ms",
        description: "Feed endpoint latency in milliseconds.");

    private static readonly Counter<long> VoteAttempts = Meter.CreateCounter<long>(
        "karar.vote.attempts",
        unit: "{vote}",
        description: "Vote endpoint attempts by outcome.");

    private static readonly Counter<long> NotificationDeliveries = Meter.CreateCounter<long>(
        "karar.notification.delivery",
        unit: "{notification}",
        description: "Push notification delivery attempts by outcome.");

    private static readonly Histogram<double> ModerationLatency = Meter.CreateHistogram<double>(
        "karar.moderation.sla",
        unit: "ms",
        description: "Synchronous moderation decision latency in milliseconds.");

    private static readonly Counter<long> RedisOperations = Meter.CreateCounter<long>(
        "karar.redis.operations",
        unit: "{operation}",
        description: "Redis operations by safe operation name and outcome.");

    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal) =>
        ActivitySource.StartActivity(name, kind);

    public static void RecordApiRequest(string route, string method, int statusCode, double elapsedMs)
    {
        var tags = new TagList
        {
            { "http.route", route },
            { "http.request.method", method },
            { "http.response.status_code", statusCode },
            { "karar.status_class", StatusClass(statusCode) }
        };

        ApiRequests.Add(1, tags);
        ApiLatency.Record(elapsedMs, tags);

        if (IsFeedRoute(method, route))
            FeedLatency.Record(elapsedMs, tags);

        if (IsVoteRoute(method, route))
            VoteAttempts.Add(1, new TagList
            {
                { "http.route", route },
                { "karar.outcome", statusCode is >= 200 and < 300 ? "success" : "failure" }
            });
    }

    public static void RecordNotificationDelivery(string outcome, string type)
    {
        NotificationDeliveries.Add(1, new TagList
        {
            { "karar.outcome", outcome },
            { "karar.notification_type", SafeNotificationType(type) }
        });
    }

    public static void RecordModeration(double elapsedMs, string outcome)
    {
        ModerationLatency.Record(elapsedMs, new TagList
        {
            { "karar.outcome", outcome }
        });
    }

    public static void RecordRedisOperation(string operation, bool success)
    {
        RedisOperations.Add(1, new TagList
        {
            { "db.system", "redis" },
            { "db.operation.name", operation },
            { "karar.outcome", success ? "success" : "failure" }
        });
    }

    public static bool IsFeedRoute(string method, string route) =>
        method.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
        (route.Equals("/api/v1/posts", StringComparison.OrdinalIgnoreCase) ||
         route.Equals("/api/v1/discover", StringComparison.OrdinalIgnoreCase));

    public static bool IsVoteRoute(string method, string route) =>
        method.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
        route.Equals("/api/v1/posts/{id:guid}/vote", StringComparison.OrdinalIgnoreCase);

    public static string StatusClass(int statusCode) => statusCode switch
    {
        >= 100 and < 200 => "1xx",
        >= 200 and < 300 => "2xx",
        >= 300 and < 400 => "3xx",
        >= 400 and < 500 => "4xx",
        >= 500 and < 600 => "5xx",
        _ => "unknown"
    };

    private static string SafeNotificationType(string type) =>
        type.Length <= 64 && type.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')
            ? type
            : "unknown";
}
