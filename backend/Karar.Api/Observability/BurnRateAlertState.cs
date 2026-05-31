using System.Collections.Concurrent;

namespace Karar.Api.Observability;

/// <summary>
/// Thread-safe in-memory store for burn-rate alert lifecycle.
/// Tracks when each policy fired, last notified, and when it resolved —
/// so the worker can deduplicate alerts within the configured cooldown window.
/// </summary>
public sealed class BurnRateAlertState
{
    private readonly ConcurrentDictionary<string, PolicyAlertRecord> _records = new();

    /// <summary>
    /// Returns true (and updates state) when an alert should be dispatched for <paramref name="policyName"/>.
    /// Fires on the first trigger and again after the cooldown has elapsed.
    /// </summary>
    public bool TryBeginAlert(string policyName, TimeSpan cooldown, out PolicyAlertRecord record)
    {
        var now = DateTimeOffset.UtcNow;
        var existing = _records.GetOrAdd(policyName, _ => new PolicyAlertRecord(policyName));

        if (existing.IsAlerting && now - existing.LastNotifiedAt < cooldown)
        {
            record = existing;
            return false;
        }

        record = existing with
        {
            IsAlerting = true,
            FiredAt = existing.IsAlerting ? existing.FiredAt : now,
            LastNotifiedAt = now,
            ResolvedAt = null
        };
        _records[policyName] = record;
        return true;
    }

    /// <summary>
    /// Returns true (and updates state) when a resolution notification should be dispatched.
    /// Only fires once per alert episode.
    /// </summary>
    public bool TryResolve(string policyName, out PolicyAlertRecord record)
    {
        if (!_records.TryGetValue(policyName, out var existing) || !existing.IsAlerting)
        {
            record = default!;
            return false;
        }

        record = existing with
        {
            IsAlerting = false,
            ResolvedAt = DateTimeOffset.UtcNow
        };
        _records[policyName] = record;
        return true;
    }

    public IReadOnlyCollection<PolicyAlertRecord> GetAll() =>
        _records.Values.OrderBy(r => r.PolicyName).ToArray();
}

public sealed record PolicyAlertRecord(string PolicyName)
{
    public bool IsAlerting { get; init; }
    public DateTimeOffset? FiredAt { get; init; }
    public DateTimeOffset LastNotifiedAt { get; init; }
    public DateTimeOffset? ResolvedAt { get; init; }
}
