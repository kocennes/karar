using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Karar.Api.Services;

/// <summary>
/// Calls Cloud Vision SafeSearch via REST API to detect unsafe image content.
/// Uses an API key (Vision:ApiKey config). Returns null when disabled or on error.
/// </summary>
public sealed class SafeSearchService(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<SafeSearchService> logger)
{
    private readonly string? _apiKey = config["Vision:ApiKey"];
    private const string Endpoint = "https://vision.googleapis.com/v1/images:annotate";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_apiKey);

    /// <summary>Returns null when skipped or on error.</summary>
    public async Task<SafeSearchResult?> AnalyzeAsync(string gcsUri, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            logger.LogDebug("SafeSearch skipped — Vision:ApiKey not configured.");
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient("vision");
            var payload = new
            {
                requests = new[]
                {
                    new
                    {
                        image = new { source = new { imageUri = gcsUri } },
                        features = new[] { new { type = "SAFE_SEARCH_DETECTION" } }
                    }
                }
            };

            var response = await client.PostAsJsonAsync($"{Endpoint}?key={_apiKey}", payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("SafeSearch API returned {Status} for {Uri}", response.StatusCode, gcsUri);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<VisionResponse>(cancellationToken: ct);
            var annotation = result?.Responses?.FirstOrDefault()?.SafeSearchAnnotation;
            if (annotation is null) return null;

            return new SafeSearchResult(
                annotation.Adult ?? "UNKNOWN",
                annotation.Violence ?? "UNKNOWN",
                annotation.Racy ?? "UNKNOWN",
                annotation.Medical ?? "UNKNOWN"
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "SafeSearch call failed for {Uri}", gcsUri);
            return null;
        }
    }

    /// <summary>
    /// Adult LIKELY+ or Violence VERY_LIKELY → block (auto_hidden).
    /// Racy VERY_LIKELY → review (under_review).
    /// </summary>
    public static (string PostStatus, string ModerationStatus) DetermineOutcome(SafeSearchResult r)
    {
        // Likelihood ordering: UNKNOWN < VERY_UNLIKELY < UNLIKELY < POSSIBLE < LIKELY < VERY_LIKELY
        if (IsAtLeast(r.Adult, "LIKELY") || IsAtLeast(r.Violence, "VERY_LIKELY"))
            return ("auto_hidden", "rejected");

        if (IsAtLeast(r.Racy, "VERY_LIKELY"))
            return ("under_review", "rejected");

        return ("active", "approved");
    }

    private static readonly Dictionary<string, int> LikelihoodOrder = new()
    {
        ["UNKNOWN"] = 0,
        ["VERY_UNLIKELY"] = 1,
        ["UNLIKELY"] = 2,
        ["POSSIBLE"] = 3,
        ["LIKELY"] = 4,
        ["VERY_LIKELY"] = 5,
    };

    private static bool IsAtLeast(string value, string threshold) =>
        LikelihoodOrder.TryGetValue(value, out var vi) &&
        LikelihoodOrder.TryGetValue(threshold, out var ti) &&
        vi >= ti;
}

public sealed record SafeSearchResult(string Adult, string Violence, string Racy, string Medical);

// ── JSON deserialization ──────────────────────────────────────────────────────

internal sealed class VisionResponse
{
    [JsonPropertyName("responses")]
    public List<VisionAnnotateResponse>? Responses { get; init; }
}

internal sealed class VisionAnnotateResponse
{
    [JsonPropertyName("safeSearchAnnotation")]
    public SafeSearchAnnotation? SafeSearchAnnotation { get; init; }
}

internal sealed class SafeSearchAnnotation
{
    [JsonPropertyName("adult")]
    public string? Adult { get; init; }

    [JsonPropertyName("violence")]
    public string? Violence { get; init; }

    [JsonPropertyName("racy")]
    public string? Racy { get; init; }

    [JsonPropertyName("medical")]
    public string? Medical { get; init; }
}
