using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

public sealed class CrisisKeywordDetectorTests
{
    // ─── Kriz kalıpları eşleşmeli ─────────────────────────────────────────────

    [Theory]
    [InlineData("intihar etmeyi düşünüyorum")]
    [InlineData("kendime zarar vermek istiyorum")]
    [InlineData("kendine zarar veriyorum")]
    [InlineData("ölmek istiyorum artık")]
    [InlineData("bitirmek istiyorum her şeyi")]
    [InlineData("hayatıma son vermek istiyorum")]
    [InlineData("hayatima son vermek istiyorum")]
    [InlineData("yaşamak istemiyorum")]
    [InlineData("yasamak istemiyorum")]
    [InlineData("INTIHAR")]
    [InlineData("Kendime Zarar Vermek")]
    public void Detect_ReturnsTrue_ForCrisisContent(string text)
    {
        CrisisKeywordDetector.Detect(text).Should().BeTrue();
    }

    [Fact]
    public void Detect_ReturnsTrue_ForCrisisWord_Ol_Standalone()
    {
        CrisisKeywordDetector.Detect("öl artık").Should().BeTrue();
    }

    // ─── Normal içerik yanlış flaglenmemeli ───────────────────────────────────

    [Theory]
    [InlineData("Arkadaşım borcunu ödemeden telefon aldı, haklı mıyım?")]
    [InlineData("Patronum beni kovdu, ne yapmalıyım?")]
    [InlineData("Dün iş görüşmesinden dönerken yağmurda ıslandım.")]
    [InlineData("Türkiye'nin tarihi çok zengindir.")]
    [InlineData("Sınavdan yüksek not aldım.")]
    [InlineData("Öldürüldü mü gerçekten?")]
    [InlineData("Film çok güzeldi, öldürdü beni dedim :)")]
    public void Detect_ReturnsFalse_ForNormalContent(string text)
    {
        CrisisKeywordDetector.Detect(text).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Detect_ReturnsFalse_ForEmptyOrWhitespace(string text)
    {
        CrisisKeywordDetector.Detect(text).Should().BeFalse();
    }

    // ─── Normalize edilmiş varyantlar (homoglyph / unicode ikamesi) ───────────

    [Fact]
    public void Detect_ReturnsTrue_ForDotlessIVariant()
    {
        // ı (dotless i, U+0131) → i: "ıntıhar" → "intihar"
        CrisisKeywordDetector.Detect("ıntıhar").Should().BeTrue();
    }

    [Theory]
    [InlineData("0lmek istiy0rum")]     // rakam 0 → o: "olmek istiyorum"
    [InlineData("k3ndim3 zarar")]       // rakam 3 → e: "kendime zarar"
    [InlineData("h@y@tım@ son")]        // @ → a + ı → i: "hayatima son"
    [InlineData("іntihar")]             // Kiril і (U+0456) → 'i': "intihar"
    [InlineData("іntіhаr")]             // Kiril і→i, а(U+0430)→a: "intihar"
    [InlineData("ІNTІHAR")]             // Kiril І (U+0406) → 'I' + lowercase: "intihar"
    public void Detect_ReturnsTrue_ForHomoglyphVariants(string text)
    {
        CrisisKeywordDetector.Detect(text).Should().BeTrue();
    }

    [Fact]
    public void Detect_ReturnsTrue_ForZeroWidthSeparatedCrisisWord()
    {
        // Sıfır genişlikli karakter (U+200B, Format) normalize sırasında çıkarılır
        CrisisKeywordDetector.Detect("int​ihar").Should().BeTrue();
    }

    // ─── Ek false-positive kontrolleri — "öl" word-boundary ─────────────────

    [Theory]
    [InlineData("Göl kenarında piknik yaptık.")]      // "göl" = lake, "öl" tek kelime değil
    [InlineData("Öldü mü haberde?")]                   // haber bağlamı, "öl" standalone değil
    [InlineData("Bölüm 5 bitti.")]                     // "bölüm" içinde "öl", standalone değil
    [InlineData("Film öldürdü beni, çok iyiydi!")]     // iltifat deyimi, "öldürdü" standalone değil
    [InlineData("Böyle bir şey öngörülmüştü.")]        // "öngörülmüştü" içinde karakter dizisi
    public void Detect_ReturnsFalse_ForOlSubstringInNonCrisisWords(string text)
    {
        CrisisKeywordDetector.Detect(text).Should().BeFalse();
    }

    // ─── ContentModerationService entegrasyonu ────────────────────────────────

    [Fact]
    public void ContentModerationService_ReturnsCrisisFlagged_ForCrisisText()
    {
        var service = new ContentModerationService();
        var result = service.Analyze("intihar etmeyi düşünüyorum");

        result.IsCrisisFlagged.Should().BeTrue();
        result.IsRejected.Should().BeFalse();
        result.Status.Should().Be("under_review");
        result.Code.Should().Be("CRISIS_FLAGGED");
    }

    [Fact]
    public void ContentModerationService_ReturnsNotCrisis_ForCleanContent()
    {
        var service = new ContentModerationService();
        var result = service.Analyze("Arkadaşımla tartıştım, haklı mıyım?");

        result.IsCrisisFlagged.Should().BeFalse();
        result.IsRejected.Should().BeFalse();
        result.Status.Should().Be("active");
    }

    [Fact]
    public void ContentModerationService_PropagatesCrisisFlag_WhenOtherReviewTriggered()
    {
        // Telefon numarası + kriz: under_review olur ve kriz bayrağı taşır
        var service = new ContentModerationService();
        var result = service.Analyze("0532 123 45 67 numarasından intihar edeceğimi söyledim");

        result.IsCrisisFlagged.Should().BeTrue();
        result.IsRejected.Should().BeFalse();
        result.Status.Should().Be("under_review");
    }

    [Fact]
    public void ContentModerationService_RejectsAutoRejectEvenIfCrisis()
    {
        // Hem bomba yapımı hem kriz varsa REJECT üstün gelir (auto-reject ilk kontrol)
        var service = new ContentModerationService();
        var result = service.Analyze("Bomba yapımı intihar etmek istiyorum");

        result.IsRejected.Should().BeTrue();
        result.Status.Should().Be("rejected");
    }
}
