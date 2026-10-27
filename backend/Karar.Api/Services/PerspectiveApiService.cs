using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Karar.Api.Common;

namespace Karar.Api.Services;

/// <summary>
/// Calls Google Perspective API to score text toxicity.
/// <para>
/// <see cref="AnalyzeAsync"/> API sorunlarında fırlatmaz; döndürdüğü <see cref="ToxicityCheck"/> metnin
/// skorlandığını, kısa olduğu için atlandığını ya da API'nin kullanılamadığını (yapılandırılmamış /
/// çağrı başarısız) söyler. Kullanılamayan API'nin moderasyon kararına etkisini
/// <see cref="ApplyToxicityResult"/> ve <c>Moderation:ToxicityUnavailableMode</c> belirler:
/// <c>review</c> → içerik incelemeye alınır (Production varsayılanı), <c>allow</c> → yalnızca anahtar
/// kelime filtresi geçerli (diğer ortamların varsayılanı; API anahtarı olmadan yerel geliştirme çalışır).
/// </para>
/// <para>
/// Not: Perspective API 2026-12-31'de kapanıyor; o tarihten sonra "kullanılamıyor" yolu kalıcı olur.
/// </para>
/// </summary>
public sealed class PerspectiveApiService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<PerspectiveApiService> logger)
{
    public const string UnavailableModeConfigKey = "Moderation:ToxicityUnavailableMode";
    public const string UnavailableModeReview = "review";
    public const string UnavailableModeAllow = "allow";

    private const long UnavailableWarningIntervalMs = 60_000;
    private const string Endpoint =
        "https://commentanalyzer.googleapis.com/v1alpha1/comments:analyze";

    private readonly string? _apiKey = configuration["Perspective:ApiKey"];
    private readonly string _unavailableMode = ResolveUnavailableMode(
        configuration[UnavailableModeConfigKey], environment.IsProduction(), logger);

    // Environment.TickCount64 (ms). Başlangıç değeri ilk uyarının hemen yazılmasını sağlar.
    private long _lastUnavailableWarningMs = -UnavailableWarningIntervalMs;

    public bool IsEnabled => !string.IsNullOrEmpty(_apiKey);

    /// <summary>Etkin mod: "review" veya "allow" (bkz. <see cref="ResolveUnavailableMode(string?, bool)"/>).</summary>
    public string UnavailableMode => _unavailableMode;

    /// <summary>
    /// Metni skorlar. API sorunlarında fırlatmaz; yalnızca çağıranın <paramref name="ct"/> iptali yayılır.
    /// </summary>
    public async Task<ToxicityCheck> AnalyzeAsync(string text, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            WarnUnavailableThrottled("yapılandırılmamış (Perspective:ApiKey yok)", null);
            return ToxicityCheck.NotConfigured;
        }

        var normalizedText = ModerationTextNormalizer.NormalizeForModeration(text);
        if (normalizedText.Length < 20) return ToxicityCheck.SkippedShortText;

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
                WarnUnavailableThrottled($"HTTP {(int)response.StatusCode}", null);
                return ToxicityCheck.Failed;
            }

            var result = await response.Content.ReadFromJsonAsync<PerspectiveResponse>(
                cancellationToken: ct);
            if (result?.AttributeScores is null)
            {
                WarnUnavailableThrottled("yanıtta attributeScores yok", null);
                return ToxicityCheck.Failed;
            }

            var toxicity         = result.AttributeScores.Toxicity?.SummaryScore?.Value ?? 0f;
            var severeToxicity   = result.AttributeScores.SevereToxicity?.SummaryScore?.Value ?? 0f;
            var identityAttack   = result.AttributeScores.IdentityAttack?.SummaryScore?.Value ?? 0f;
            var insult           = result.AttributeScores.Insult?.SummaryScore?.Value ?? 0f;
            var threat           = result.AttributeScores.Threat?.SummaryScore?.Value ?? 0f;

            // Weighted: threat and identity_attack carry extra weight; cap at 1.0
            var overall = Math.Min(1f, Math.Max(toxicity,
                Math.Max(threat * 1.2f, identityAttack * 1.1f)));

            return ToxicityCheck.Scored(
                new PerspectiveResult(toxicity, severeToxicity, identityAttack, insult, threat, overall));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Çağıran iptal etti; bu bir moderasyon sonucu değil.
        }
        catch (Exception ex)
        {
            // HttpClient zaman aşımı da TaskCanceledException fırlatır; çağıranın iptali değilse bu bir
            // API hatasıdır: içerik oluşturma 500 ile çökmemeli, karar ApplyToxicityResult'a kalmalı.
            WarnUnavailableThrottled("çağrı başarısız", ex);
            return ToxicityCheck.Failed;
        }
    }

    /// <summary>
    /// Perspective sonucunu anahtar kelime kararının üzerine uygular. Gönderi ve yorum oluşturma
    /// (ve ileride düzenleme moderasyonu eklenirse düzenleme) Perspective'i çağırıyorsa kararı bu
    /// helper'dan geçirmeli; eşik ve kullanılamama mantığı handler'larda tekrarlanmamalı.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Skor "rejected" eşiğinde → rejected (CONTENT_REJECTED), moddan bağımsız.</item>
    /// <item>Skor "under_review" eşiğinde ve mevcut karar active → under_review.</item>
    /// <item>API kullanılamıyor (yapılandırılmamış / başarısız): <paramref name="unavailableMode"/> "review"
    /// ise active karar under_review olur; "allow" ise karar değişmez (yalnızca anahtar kelime filtresi).</item>
    /// <item>Metin kısa olduğu için atlandıysa karar değişmez; bu bir eksiklik değil bilinçli atlamadır.</item>
    /// <item>Zaten rejected / under_review olan karar (kriz, kişisel bilgi…) mesajı ve bayraklarıyla korunur.</item>
    /// </list>
    /// </remarks>
    public static ModerationDecision ApplyToxicityResult(
        ModerationDecision current,
        ToxicityCheck check,
        string unavailableMode)
    {
        if (current.IsRejected) return current;

        if (check.Result is { } result)
        {
            var status = DetermineStatus(result);
            if (status == "rejected")
                return ModerationDecision.Reject("CONTENT_REJECTED", "İçerik politikası gereği bu metin yayınlanamaz.");
            if (status == "under_review" && current.Status == "active")
                return ModerationDecision.Review("İçerik moderasyon incelemesine alındı.", current.IsCrisisFlagged);
            return current;
        }

        var failClosed = string.Equals(unavailableMode, UnavailableModeReview, StringComparison.OrdinalIgnoreCase);
        if (check.IsUnavailable && failClosed && current.Status == "active")
            return ModerationDecision.Review("İçerik moderasyon incelemesine alındı.", current.IsCrisisFlagged);

        return current;
    }

    /// <summary>
    /// "review" / "allow" (büyük-küçük harf ve boşluk duyarsız). Boş veya tanınmayan değerde
    /// Production'da "review", diğer ortamlarda "allow".
    /// </summary>
    public static string ResolveUnavailableMode(string? configured, bool isProduction)
    {
        var mode = configured?.Trim().ToLowerInvariant();
        if (mode is UnavailableModeReview or UnavailableModeAllow) return mode;
        return isProduction ? UnavailableModeReview : UnavailableModeAllow;
    }

    private static string ResolveUnavailableMode(string? configured, bool isProduction, ILogger logger)
    {
        var mode = ResolveUnavailableMode(configured, isProduction);
        if (!string.IsNullOrWhiteSpace(configured)
            && !string.Equals(configured.Trim(), mode, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "{Key} değeri tanınmadı: '{Configured}'. Ortam varsayılanı '{Mode}' kullanılıyor.",
                UnavailableModeConfigKey, configured, mode);
        }

        return mode;
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

    // Her istek değil, en fazla dakikada bir uyarı; neden ("yapılandırılmamış" / "HTTP 503" / "çağrı başarısız")
    // mesajda belirtilir ki yapılandırma eksikliği ile servis arızası loglarda ayırt edilsin.
    private void WarnUnavailableThrottled(string reason, Exception? exception)
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastUnavailableWarningMs);
        if (now - last < UnavailableWarningIntervalMs) return;
        if (Interlocked.CompareExchange(ref _lastUnavailableWarningMs, now, last) != last) return;

        logger.LogWarning(exception,
            "Perspective API kullanılamıyor ({Reason}); toksisite skoru olmadan oluşturulan içerik için {Key}={Mode}. Bu uyarı en fazla dakikada bir yazılır.",
            reason, UnavailableModeConfigKey, _unavailableMode);
    }
}

/// <summary>Perspective çağrısının sonucu: skor ya da skorun neden olmadığı.</summary>
public enum ToxicityCheckOutcome
{
    /// <summary>Perspective yanıt verdi; <see cref="ToxicityCheck.Result"/> dolu.</summary>
    Scored,

    /// <summary>Metin çok kısa; Perspective bilinçli olarak atlandı, anahtar kelime filtresi yeterli.</summary>
    SkippedShortText,

    /// <summary>Perspective:ApiKey yapılandırılmamış.</summary>
    NotConfigured,

    /// <summary>HTTP hatası, zaman aşımı veya istisna.</summary>
    Failed
}

public sealed record ToxicityCheck(ToxicityCheckOutcome Outcome, PerspectiveResult? Result)
{
    public static readonly ToxicityCheck NotConfigured = new(ToxicityCheckOutcome.NotConfigured, null);
    public static readonly ToxicityCheck SkippedShortText = new(ToxicityCheckOutcome.SkippedShortText, null);
    public static readonly ToxicityCheck Failed = new(ToxicityCheckOutcome.Failed, null);

    public static ToxicityCheck Scored(PerspectiveResult result) => new(ToxicityCheckOutcome.Scored, result);

    /// <summary>Skor yok ve bu "görüş yok" değil bir eksiklik: API yapılandırılmamış ya da çağrı başarısız.</summary>
    public bool IsUnavailable => Outcome is ToxicityCheckOutcome.NotConfigured or ToxicityCheckOutcome.Failed;
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
