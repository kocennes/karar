using System.Text.RegularExpressions;
using Karar.Api.Common;

namespace Karar.Api.Services;

public sealed class ContentModerationService
{
    private static readonly Regex TurkishPhonePattern = new(
        @"(\+90|0)?\s?5\d{2}\s?\d{3}\s?\d{2}\s?\d{2}",
        RegexOptions.Compiled
    );

    private static readonly Regex EmailPattern = new(
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // ── Doxxing: raw-text patterns (digit normalization must NOT run here) ──────
    // TC Kimlik: 11 haneli, ilk basamak 0 olamaz
    private static readonly Regex TcKimlikPattern = new(
        @"\b[1-9]\d{10}\b",
        RegexOptions.Compiled
    );

    // IBAN: TR + 24 rakam (boşluklu veya boşluksuz)
    private static readonly Regex IbanPattern = new(
        @"\bTR\s?\d{2}[\s\d]{22,30}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    // Kredi/banka kartı: 16 rakam, aralarında boşluk veya tire olabilir
    private static readonly Regex CreditCardPattern = new(
        @"\b\d{4}[\s\-]\d{4}[\s\-]\d{4}[\s\-]\d{4}\b",
        RegexOptions.Compiled
    );

    // Soft-identifying field patterns
    private static readonly Regex AgePattern = new(
        @"\b(\d{1,2}\s*(yaş(ında|ındaki|lı|li)?|yıl(lık|lığında)?)|"
        + @"(otuz|kırk|elli|yirmi|on\s*yedi|on\s*sekiz)\s*yaş)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex OccupationPattern = new(
        @"\b(öğretmen|mühendis|avukat|doktor|hemşire|eczacı|muhasebeci|müdür|"
        + @"şef|uzman|asistan|stajyer|memur|polis|asker|esnaf|tüccar|yönetici|"
        + @"direktör|patron|çalışan|işçi|teknisyen|mimar|ressam|yazar|gazeteci|"
        + @"akademisyen|araştırmacı|danışman|analist|operatör|sekreter|noter)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex LocationPattern = new(
        @"\b(kadıköy|beşiktaş|üsküdar|bağcılar|pendik|maltepe|kartal|ümraniye|"
        + @"şişli|beyoğlu|fatih|eyüp|gaziosmanpaşa|bakırköy|bahçelievler|"
        + @"çankaya|keçiören|mamak|etimesgut|yenimahalle|sincan|ankara|izmir|"
        + @"bursa|adana|antalya|konya|mahallesi|semtinde|ilçesi|bölgesinde|"
        + @"sokak|cadde|sitesi|apartman)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex PhysicalPattern = new(
        @"\b(uzun\s*boylu|kısa\s*boylu|sarışın|esmer|kumral|kızıl\s*(saçlı)?|"
        + @"kilolu|zayıf|şişman|kel|gözlüklü|sakallı|boyunca|cm|kilo|kg|"
        + @"\d{2,3}\s*(cm|kilo|kg)|gözleri\s*(mavi|yeşil|kahve|siyah|ela))\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex FamilyStatusPattern = new(
        @"\b(evli|bekar|dul|nişanlı|boşanmış|çocuklu|çocuksuz|"
        + @"\d+\s*çocuk(lu)?|eşi|kocası|karısı|annesi|babası|abisi|ablası|"
        + @"kayınvalidesi|kayınpederi)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly Regex InstitutionPattern = new(
        @"\b(üniversitesi|fakültesi|lisesi|okulu|hastahanesi|hastanesi|"
        + @"şirketi|firması|a\.ş\.|ltd\.|holding|bankası|belediyesi|"
        + @"bakanlığı|müdürlüğü|genel\s*müdür)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    private static readonly string[] AutoRejectTerms =
    [
        // Önceden var olan — şiddet ve istismar
        "çocuk istismarı",
        "intihar yöntemi",
        "bomba yapımı",

        // Başkasına yönelik zarar çağrısı (cinayete teşvik / açık tehdit)
        "kendini öldür",
        "kendını oldur",
        "git öl",
        "git ol",
        "öl git",
        "ol git",
        "defol git öl",
        "seni öldüreceğim",
        "seni oldüreceğim",
        "sizi öldüreceğim",
        "öldüreceğim seni",

        // Cinsel taciz — açık cinsel tehdit ve hakaret kalıpları
        "tecavüz edeceğim",
        "tecavuz edecegim",
        "sana tecavüz",
        "cinsel saldırı yapacağım",

        // Kimlik temelli nefret söylemi — etnik/dini/cinsiyet slurları
        // (Bu terimler bağımsız olarak nefret içeriklidir; bağlam önemli değildir)
        "ermenileri öldür",
        "kürtleri öldür",
        "yahudileri öldür",
        "rumları öldür",
        "müslümanları öldür",
        "hristiyanları öldür",
        "kafiri öldür",
        "gavuru öldür",
        "kadını dövün",
        "kadınları dövün",
        "eşcinselleri öldür",
        "ibneyi öldür",
        "lezbiyeni öldür",
    ];

    private static readonly string[] NormalizedAutoRejectTerms =
        AutoRejectTerms.Select(NormalizeTerm).ToArray();

    private static readonly string[] ReviewTerms =
    [
        "adresim",
        "tc kimlik",
        "kimlik numarası",
        "kendime zarar",
        "öldürmek istiyorum",
        "tehdit etti"
    ];

    private static readonly string[] NormalizedReviewTerms =
        ReviewTerms.Select(NormalizeTerm).ToArray();

    public ModerationDecision Analyze(string text)
    {
        var normalized = ModerationTextNormalizer
            .NormalizeForModeration(text)
            .ToLowerInvariant();

        // Kriz sinyali: Perspective API öncesinde çalışır, cezalandırıcı değil destek odaklı.
        // Diğer moderasyon kurallarından bağımsız olarak işaretlenir.
        var isCrisis = CrisisKeywordDetector.Detect(text);

        // Doxxing: TC kimlik / IBAN / kredi kartı — rakamlar normalize edilmeden önce raw text üzerinde kontrol.
        // Bu kalıplar rakam içerdiğinden normalizer'ın rakam→harf dönüşümü (0→o, 5→s) uygulanmamalı.
        if (TcKimlikPattern.IsMatch(text) || IbanPattern.IsMatch(text) || CreditCardPattern.IsMatch(text))
        {
            return ModerationDecision.Reject("CONTENT_REJECTED", "İçerik politikası gereği bu metin yayınlanamaz.");
        }

        if (NormalizedAutoRejectTerms.Any(normalized.Contains))
        {
            return ModerationDecision.Reject("CONTENT_REJECTED", "İçerik politikası gereği bu metin yayınlanamaz.");
        }

        if (TurkishPhonePattern.IsMatch(normalized) || EmailPattern.IsMatch(normalized))
        {
            return ModerationDecision.Review("Kişisel bilgi olabilecek veri tespit edildi.", isCrisis);
        }

        if (NormalizedReviewTerms.Any(normalized.Contains))
        {
            return ModerationDecision.Review("İçerik moderasyon incelemesine alındı.", isCrisis);
        }

        if (HasSoftIdentifyingCombination(normalized))
        {
            return ModerationDecision.Review("İçerik birden fazla kişisel tanımlayıcı bilgi içeriyor.", isCrisis);
        }

        if (isCrisis)
        {
            return ModerationDecision.CrisisReview();
        }

        return ModerationDecision.Active();
    }

    private static bool HasSoftIdentifyingCombination(string text)
    {
        var matchCount = 0;

        if (AgePattern.IsMatch(text)) matchCount++;
        if (OccupationPattern.IsMatch(text)) matchCount++;
        if (LocationPattern.IsMatch(text)) matchCount++;
        if (PhysicalPattern.IsMatch(text)) matchCount++;
        if (FamilyStatusPattern.IsMatch(text)) matchCount++;
        if (InstitutionPattern.IsMatch(text)) matchCount++;

        return matchCount >= 3;
    }

    private static string NormalizeTerm(string term) =>
        ModerationTextNormalizer.NormalizeForModeration(term).ToLowerInvariant();
}

public sealed record ModerationDecision(
    string Status,
    bool IsRejected,
    string? Code,
    string Message,
    bool IsCrisisFlagged = false
)
{
    public static ModerationDecision Active() =>
        new("active", false, null, "İçerik yayınlandı.");

    public static ModerationDecision Review(string message, bool isCrisisFlagged = false) =>
        new("under_review", false, null, message, isCrisisFlagged);

    public static ModerationDecision CrisisReview() =>
        new("under_review", false, "CRISIS_FLAGGED", "İçerik moderasyon incelemesine alındı.", true);

    public static ModerationDecision Reject(string code, string message) =>
        new("rejected", true, code, message);
}
