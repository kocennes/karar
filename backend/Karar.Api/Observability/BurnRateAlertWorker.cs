using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Karar.Api.Observability;

/// <summary>
/// Periodically evaluates burn-rate policies and dispatches alerts when a policy
/// threshold is exceeded. Handles both fast-burn (page) and slow-burn (slack) policies.
///
/// Alert lifecycle:
///   ok  → alert : dispatch "firing" notification (subject to cooldown on repeat fires)
///   alert → ok  : dispatch "resolved" notification (once per episode)
///
/// Dispatch channel is determined by the policy's Severity field:
///   "page"  → Slo:PageWebhookUrl (if configured), else Error log
///   "slack" → Slo:SlackWebhookUrl (if configured), else Warning log
/// </summary>
public sealed class BurnRateAlertWorker(
    SloMetrics sloMetrics,
    BurnRateAlertState alertState,
    IOptions<SloOptions> options,
    IHttpClientFactory httpFactory,
    ILogger<BurnRateAlertWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("BurnRateAlertWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await EvaluateAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "BurnRateAlertWorker evaluation error.");
            }

            await Task.Delay(options.Value.AlertCheckInterval, stoppingToken);
        }
    }

    private async Task EvaluateAsync(CancellationToken ct)
    {
        var snapshot = sloMetrics.GetSnapshot();
        var opts = options.Value;

        foreach (var burnRate in snapshot.BurnRatePolicies)
        {
            var policy = opts.BurnRatePolicies.FirstOrDefault(p => p.Name == burnRate.Name);
            if (policy is null) continue;

            var cooldown = policy.Severity == "page" ? opts.PageAlertCooldown : opts.SlackAlertCooldown;

            if (burnRate.Status == "alert")
            {
                if (alertState.TryBeginAlert(burnRate.Name, cooldown, out var record))
                    await DispatchAlertAsync(burnRate, policy, isFiring: true, ct);
            }
            else
            {
                if (alertState.TryResolve(burnRate.Name, out _))
                    await DispatchAlertAsync(burnRate, policy, isFiring: false, ct);
            }
        }
    }

    private async Task DispatchAlertAsync(
        BurnRateSnapshot burnRate,
        BurnRatePolicy policy,
        bool isFiring,
        CancellationToken ct)
    {
        var opts = options.Value;
        var webhookUrl = policy.Severity == "page" ? opts.PageWebhookUrl : opts.SlackWebhookUrl;
        var emoji = isFiring ? "🔴" : "✅";
        var state = isFiring ? "FIRING" : "RESOLVED";

        var summary = $"{emoji} [{state}] SLO burn-rate policy \"{burnRate.Name}\" " +
                      $"(severity={policy.Severity}, window={burnRate.Window.TotalMinutes:0}m, " +
                      $"threshold={burnRate.Threshold:0.0}x, actual={burnRate.BurnRate:0.00}x, " +
                      $"samples={burnRate.SampleCount})";

        if (isFiring)
            LogFiring(policy.Severity, summary);
        else
            logger.LogInformation("{Summary}", summary);

        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        try
        {
            using var client = httpFactory.CreateClient("slo-alerts");
            var payload = BuildPayload(policy.Severity, summary, burnRate, isFiring);
            using var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to deliver burn-rate alert to webhook ({Severity}).", policy.Severity);
        }
    }

    private void LogFiring(string severity, string summary)
    {
        if (severity == "page")
            logger.LogError("{Summary}", summary);
        else
            logger.LogWarning("{Summary}", summary);
    }

    private static object BuildPayload(
        string severity,
        string summary,
        BurnRateSnapshot burnRate,
        bool isFiring)
    {
        if (severity == "slack")
        {
            return new
            {
                text = summary,
                attachments = new[]
                {
                    new
                    {
                        color = isFiring ? "#FF0000" : "#36A64F",
                        fields = new[]
                        {
                            new { title = "Policy", value = burnRate.Name, @short = true },
                            new { title = "Burn Rate", value = $"{burnRate.BurnRate:0.00}x", @short = true },
                            new { title = "Threshold", value = $"{burnRate.Threshold:0.0}x", @short = true },
                            new { title = "Window", value = $"{burnRate.Window.TotalMinutes:0}m", @short = true },
                            new { title = "Samples", value = burnRate.SampleCount.ToString(), @short = true }
                        }
                    }
                }
            };
        }

        // Generic JSON payload compatible with PagerDuty Events v2 / generic webhooks
        return new
        {
            routing_key = (string?)null,
            event_action = isFiring ? "trigger" : "resolve",
            dedup_key = $"karar-slo-{burnRate.Name}",
            payload = new
            {
                summary,
                severity = "critical",
                source = "karar-api",
                custom_details = new
                {
                    policy_name = burnRate.Name,
                    burn_rate = burnRate.BurnRate,
                    threshold = burnRate.Threshold,
                    window_minutes = burnRate.Window.TotalMinutes,
                    sample_count = burnRate.SampleCount
                }
            }
        };
    }
}
