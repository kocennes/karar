using System.Diagnostics.Metrics;

namespace Karar.Api.Services;

/// <summary>
/// Service for tracking high-level business metrics.
/// These are exported to Cloud Monitoring via OpenTelemetry or System.Diagnostics.Metrics.
/// </summary>
public sealed class BusinessMetricsService
{
    private readonly Counter<long> _postsCreated;
    private readonly Counter<long> _votesCast;
    private readonly Counter<long> _contentRejected;
    private readonly ObservableGauge<int> _moderationQueueDepth;

    private int _currentQueueDepth;

    public BusinessMetricsService(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Karar.Api.Business");

        _postsCreated = meter.CreateCounter<long>(
            "karar.posts.created",
            unit: "{post}",
            description: "Toplam oluşturulan gönderi sayısı");

        _votesCast = meter.CreateCounter<long>(
            "karar.votes.cast",
            unit: "{vote}",
            description: "Toplam kullanılan oy sayısı");

        _contentRejected = meter.CreateCounter<long>(
            "karar.content.rejected",
            unit: "{item}",
            description: "Moderasyon tarafından reddedilen içerik sayısı");

        _moderationQueueDepth = meter.CreateObservableGauge<int>(
            "karar.moderation.queue_depth",
            () => _currentQueueDepth,
            unit: "{item}",
            description: "Bekleyen moderasyon kuyruğu derinliği");
    }

    public void RecordPostCreated(string category) =>
        _postsCreated.Add(1, new KeyValuePair<string, object?>("category", category));

    public void RecordVoteCast(string type) =>
        _votesCast.Add(1, new KeyValuePair<string, object?>("type", type));

    public void RecordContentRejected(string reason) =>
        _contentRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void UpdateModerationQueueDepth(int depth) =>
        _currentQueueDepth = depth;
}
