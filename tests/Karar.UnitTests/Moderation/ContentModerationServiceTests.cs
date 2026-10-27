using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

public sealed class ContentModerationServiceTests
{
    private readonly ContentModerationService _service = new();

    // ── Temiz içerik ───────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_ReturnsActive_ForCleanConflict()
    {
        var result = _service.Analyze(
            "Arkadaşım borcunu ödemeden telefon aldı, bunu hatırlatmakta haklı mıyım?"
        );

        result.Status.Should().Be("active");
        result.IsRejected.Should().BeFalse();
    }

    // ── Telefon / e-posta → inceleme ──────────────────────────────────────────

    [Fact]
    public void Analyze_ReturnsUnderReview_ForPhoneNumber()
    {
        var result = _service.Analyze(
            "Bana 0532 123 45 67 numarasından ulaşıp tehdit etti."
        );

        result.Status.Should().Be("under_review");
        result.IsRejected.Should().BeFalse();
    }

    // ── Mevcut AutoReject ─────────────────────────────────────────────────────

    [Fact]
    public void Analyze_RejectsForbiddenPolicyContent()
    {
        var result = _service.Analyze("Bomba yapımı hakkında detaylı bilgi istiyorum.");

        result.Status.Should().Be("rejected");
        result.IsRejected.Should().BeTrue();
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    // ── Doxxing: TC Kimlik Numarası ───────────────────────────────────────────

    [Theory]
    [InlineData("TC kimlik numaram 12345678901")]
    [InlineData("Dosyada 98765432100 kimlik numarası geçiyor")]
    [InlineData("Bu kişinin 34567890123 TC'si paylaşıldı")]
    public void Analyze_Rejects_TcKimlikNumber(string text)
    {
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeTrue(because: "TC kimlik numarası doxxing içerik olarak reddedilmeli");
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    [Theory]
    [InlineData("2024 yılında 1234567 kayıt numarası verildi")]      // 7 haneli — TC değil
    [InlineData("Şube kodu 01234 olan banka şubesi")]                 // 5 haneli, 0 ile başlıyor
    [InlineData("Fatura no: 1234567890 geldi")]                       // 10 haneli
    public void Analyze_DoesNotReject_SimilarLookingNumbers(string text)
    {
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeFalse(because: "kısa veya sıfırla başlayan sayılar TC kimlik değildir");
    }

    // ── Doxxing: IBAN ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("IBAN: TR33 0006 1005 1978 6457 8413 26")]
    [InlineData("Havale için TR330006100519786457841326 kullan")]
    [InlineData("tr33 0006 1005 1978 6457 8413 26")]
    public void Analyze_Rejects_IbanNumber(string text)
    {
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeTrue(because: "IBAN numarası ciddi doxxing içeriğidir");
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    // ── Doxxing: Kredi kartı ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Kartım: 4111 1111 1111 1111")]
    [InlineData("Ödeme için 5500-0000-0000-0004 kullan")]
    public void Analyze_Rejects_CreditCardNumber(string text)
    {
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeTrue(because: "kredi kartı numarası paylaşımı ciddi güvenlik ihlaline yol açar");
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    // ── Başkasına yönelik zarar çağrısı ──────────────────────────────────────

    [Theory]
    [InlineData("git öl artık")]
    [InlineData("kendini öldür")]
    [InlineData("defol git öl")]
    [InlineData("seni öldüreceğim")]
    [InlineData("öldüreceğim seni")]
    public void Analyze_Rejects_DirectedHarmCalls(string text)
    {
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeTrue(because: "başkasına yönelik zarar çağrısı yayın öncesi durdurulmalı");
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    // ── Cinsel taciz ──────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_Rejects_SexualAssaultThreats()
    {
        var result = _service.Analyze("sana tecavüz edeceğim");

        result.IsRejected.Should().BeTrue();
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    // ── Kimlik temelli nefret söylemi ─────────────────────────────────────────

    [Theory]
    [InlineData("ermenileri öldür")]
    [InlineData("kürtleri öldür")]
    [InlineData("eşcinselleri öldür")]
    [InlineData("kadınları dövün")]
    public void Analyze_Rejects_IdentityBasedHateSpeech(string text)
    {
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeTrue(because: "kimlik temelli nefret söylemi yayın öncesi reddedilmeli");
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    // ── Normalizer bypass denemeleri ─────────────────────────────────────────

    [Theory]
    [InlineData("gіt öl")]                  // Kiril і (U+0456) → i: "git öl"
    [InlineData("kendını öldür")]            // ı→i: "kendini öldür"
    [InlineData("$eni öldüreceğim")]         // $→s: "seni öldüreceğim"
    [InlineData("$enі öldüreceğim")]         // $→s ve Kiril і→i: "seni öldüreceğim"
    public void Analyze_Rejects_HomoglyphHarmCalls(string text)
    {
        // ModerationTextNormalizer Kiril / homoglyph karakterleri düzelttiğinde yasaklı kalıplar eşleşmeli
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeTrue(because: "homoglyph bypass denemesi moderasyonu atlatmamalı");
    }

    // ── Doxxing raw-text kontrolü — normalizer rakam dönüştürmemeli ───────────

    [Fact]
    public void Analyze_Rejects_TcKimlik_BeforeNormalization()
    {
        // Normalizer 0→o, 5→s, 3→e dönüştürür — ama TC kimlik ham metin üzerinde kontrol edilmeli
        const string text = "Kişinin TC numarası 12345678901 olarak kayıtlı.";
        var result = _service.Analyze(text);

        result.IsRejected.Should().BeTrue(because: "TC kimlik kontrolü digit normalizasyonundan önce çalışmalı");
    }

    // ── Doxxing: telefon / e-posta — ham metin kontrolü ───────────────────────
    // Normalizer rakamları ve '@' işaretini harfe çevirir (0→o, 1→l, 3→e, 5→s, 7→t, @→a).
    // Bu yüzden telefon (5xx ile başlar) ve e-posta ('@' ister) kalıpları normalize metinde
    // hiçbir zaman eşleşemez; ham metin üzerinde aranmaları gerekir. Cümlelerde başka bir
    // inceleme tetikleyicisi (ör. "tehdit etti") bilinçli olarak yok: karar kalıptan gelmeli.

    [Theory]
    [InlineData("Beni 0532 123 45 67 numarasından arayabilirsiniz, haksız mıyım?")]
    [InlineData("Beni +90 532 123 45 67 numarasından arayabilirsiniz, haksız mıyım?")]
    [InlineData("Beni 05321234567 numarasından arayabilirsiniz, haksız mıyım?")]
    [InlineData("Beni 0 (532) 123 45 67 numarasından arayabilirsiniz, haksız mıyım?")]
    [InlineData("Beni 0532-123-45-67 numarasından arayabilirsiniz, haksız mıyım?")]
    public void Analyze_ReturnsUnderReview_ForTurkishMobileNumber_WithoutOtherTriggers(string text)
    {
        var result = _service.Analyze(text);

        result.Status.Should().Be("under_review", because: "telefon numarası tek başına kişisel bilgi incelemesi tetiklemeli");
        result.IsRejected.Should().BeFalse();
        result.Code.Should().BeNull();
        result.Message.Should().Be("Kişisel bilgi olabilecek veri tespit edildi.",
            because: "karar telefon kalıbından gelmeli, başka bir inceleme kuralından değil");
    }

    [Fact]
    public void Analyze_ReturnsUnderReview_ForEmailAddress_WithoutOtherTriggers()
    {
        var result = _service.Analyze("Bana ali.veli@gmail.com üzerinden yazabilirsiniz, haksız mıyım?");

        result.Status.Should().Be("under_review", because: "e-posta adresi tek başına kişisel bilgi incelemesi tetiklemeli");
        result.IsRejected.Should().BeFalse();
        result.Code.Should().BeNull();
        result.Message.Should().Be("Kişisel bilgi olabilecek veri tespit edildi.");
    }

    [Fact]
    public void Analyze_DoesNotDetect_ObfuscatedEmail_KnownLimitation()
    {
        // "[at]" / "[dot]" ile gizlenmiş e-posta bilinçli olarak kapsam dışı: EmailPattern gerçek '@' ister.
        // Bu test mevcut davranışı belgeler; gizlenmiş e-posta tespiti eklenirse beklenti under_review olmalı.
        var result = _service.Analyze("Bana ali [at] gmail [dot] com üzerinden yazabilirsiniz, haksız mıyım?");

        result.Status.Should().Be("active");
    }

    [Fact]
    public void Analyze_Rejects_TcKimlikLikeNumber_InRawText()
    {
        var result = _service.Analyze("Komşumun kimliğindeki numara 23456789012, herkes öğrensin.");

        result.IsRejected.Should().BeTrue();
        result.Code.Should().Be("CONTENT_REJECTED");
    }

    [Fact]
    public void Analyze_Rejects_IbanWithSpaces_InRawText()
    {
        var result = _service.Analyze("Parayı TR12 0006 1005 1978 6457 8413 26 hesabına yatırmış, haklı mı?");

        result.IsRejected.Should().BeTrue();
        result.Code.Should().Be("CONTENT_REJECTED");
    }
}
