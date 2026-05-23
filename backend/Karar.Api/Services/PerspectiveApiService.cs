using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Karar.Api.Common;

namespace Karar.Api.Services;

/// <summary>
/// Calls Google Perspective API to score text toxicity.
/// Returns null (skip) when the API key is not configured or the call fails,
/// so the keyword-based fallback in ContentModerationService still governs.
/// </summary>
public sealed class PerspectiveApiService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<PerspectiveApiService> logger)
{
    private readonly string? _apiKey = configuration["Perspective:ApiKey"];
    private const string Endpoint =
        "https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze";

    public bool IsEnabled => !string.IsNullOrEmpty(_apiKey);

    /// <summary>Returns null when skipped or on error.</summary>
    public async Task<PerspectiveResult?> AnalyzeAsync(string text, CancellationToken ct = default)
    {
        var normalizedText = ModerationTextNormalizer.NormalizeForModeration(text);
        if (!IsEnabled || normalizedText.Length < 20) return null;

        try
        {
            var client = httpClientFactory.CreateClient("perspective");
            var payload = new
            {
                comment = new { text = normalizedText },
                languages = new[] { "tr" },
                requestedAttributes = new
                {
                    TOXICITY = new { },
                    SEVERE_TOXICITY = new { },
                    IDENTITY_ATTACK = new { },
                    INSULT = new { },
                    THREAT = new { }
                }
            };

            var response = await client.PostAsJsonAsync($"{Endpoint}?key={_apiKey}", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Perspective API returned {Status}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<PerspectiveResponse>(
                cancellationToken: ct);
            if (result?.AttributeScores is null) return null;

            var toxicity         = result.AttributeScores.Toxicity?.SummaryScore?.Value ?? 0f;
            var severeToxicity   = result.AttributeScores.SevereToxicity?.SummaryScore?.Value ?? 0f;
            var identityAttack   = result.AttributeScores.IdentityAttack?.SummaryScore?.Value ?? 0f;
            var insult           = result.AttributeScores.Insult?.SummaryScore?.Value ?? 0f;
            var threat           = result.AttributeScores.Threat?.SummaryScore?.Value ?? 0f;

            // Weighted: threat and identity_attack carry extra weight; cap at 1.0
            var overall = Math.Min(1f, Math.Max(toxicity,
                Math.Max(threat * 1.2f, identityAttack * 1.1f)));

            return new PerspectiveResult(toxicity, severeToxicity, identityAttack, insult, threat, overall);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Perspective API call failed; skipping");
            return null;
        }
    }

    /// <summary>Converts a perspective result into a moderation status string.</summary>
    public static string DetermineStatus(PerspectiveResult result)
    {
        if (result.Overall >= 0.90f || result.SevereToxicity >= 0.85f)
            return "rejected";

        if (result.Overall >= 0.70f || result.Threat >= 0.80f)
            return "under_review";

        return "active";
    }
}

public sealed record PerspectiveResult(
    float Toxicity,
    float SevereToxicity,
    float IdentityAttack,
    float Insult,
    float Threat,
    float Overall
);

// ── JSON deserialization models ───────────────────────────────────────────────

internal sealed class PerspectiveResponse
{
    [JsonPropertyName("attributeScores")]
    public AttributeScores? AttributeScores { get; init; }
}

internal sealed class AttributeScores
{
    [JsonPropertyName("TOXICITY")]
    public AttributeScore? Toxicity { get; init; }

    [JsonPropertyName("SEVERE_TOXICITY")]
    public AttributeScore? SevereToxicity { get; init; }

    [JsonPropertyName("IDENTITY_ATTACK")]
    public AttributeScore? IdentityAttack { get; init; }

    [JsonPropertyName("INSULT")]
    public AttributeScore? Insult { get; init; }

    [JsonPropertyName("THREAT")]
    public AttributeScore? Threat { get; init; }
}

internal sealed class AttributeScore
{
    [JsonPropertyName("summaryScore")]
    public SummaryScore? SummaryScore { get; init; }
}

internal sealed class SummaryScore
{
    [JsonPropertyName("value")]
    public float Value { get; init; }
}
