using System.Text.RegularExpressions;
using Karar.Api.Common;

namespace Karar.Api.Services;

/// <summary>
/// Türkçe kriz anahtar kelime tespiti — Perspective API öncesi çalışan senkron pre-filter.
/// Taciz/nefret/doxxing kurallarından bağımsız, yalnızca güvenlik ve destek odaklıdır.
/// Eşleşen içerik cezalandırılmaz; moderasyon kuyruğuna yüksek öncelikle alınır ve
/// içerik sahibine destek bildirimi gönderilir.
/// </summary>
public static class CrisisKeywordDetector
{
    // Exact-phrase patterns: basit substring eşleşmesi için normalize edilmiş terimler.
    // Her terim ModerationTextNormalizer üzerinden geçirilir.
    private static readonly string[] CrisisTerms =
    [
        "intihar",
        "kendime zarar",
        "kendine zarar",
        "ölmek istiyorum",
        "olmek istiyorum",
        "bitirmek istiyorum",
        "hayatıma son",
        "hayatima son",
        "yaşamak istemiyorum",
        "yasamak istemiyorum",
        "öldürmek istiyorum kendimi",
        "oldürmek istiyorum kendimi",
    ];

    // Tek kelime "öl" — sadece tam kelime olarak, çekim ekleriyle eşleşme önlemek için.
    // "öldürüldü", "öldü" gibi haber bildiren ifadeleri tetiklememeli.
    private static readonly Regex CrisisWordBoundaryPattern = new(
        @"\böl\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly string[] NormalizedCrisisTerms =
        CrisisTerms
            .Select(t => ModerationTextNormalizer.NormalizeForModeration(t).ToLowerInvariant())
            .ToArray();

    /// <summary>
    /// İçeriği kriz keyword listesine göre tarar.
    /// true döndüğünde içerik destek sinyali içeriyor kabul edilir.
    /// </summary>
    public static bool Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var normalized = ModerationTextNormalizer.NormalizeForModeration(text).ToLowerInvariant();

        if (NormalizedCrisisTerms.Any(normalized.Contains))
            return true;

        if (CrisisWordBoundaryPattern.IsMatch(normalized))
            return true;

        return false;
    }
}
