using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

// Regression: the phone and e-mail PII rules ran on the leetspeak-normalized text, where
// '5'→'s', '0'→'o' and '@'→'a'. A Turkish mobile number ("5xx ...") or an e-mail address
// could therefore never match, and doxxing content went live unless another keyword
// happened to trigger review.
public sealed class ContentModerationPiiTests
{
    private readonly ContentModerationService _service = new();

    [Theory]
    [InlineData("Bu adamı 0532 123 45 67 numarasından arayın")]
    [InlineData("Numarası +90 532 123 45 67, herkes yazsın")]
    [InlineData("Numarası 05321234567 bilginize")]
    [InlineData("Numarası ０５３２ １２３ ４５ ６７ bilginize")]
    public void Analyze_HoldsPhoneNumberForReview(string text)
    {
        var decision = _service.Analyze(text);

        decision.Status.Should().Be("under_review");
        decision.IsRejected.Should().BeFalse();
    }

    [Theory]
    [InlineData("Mail adresi ornek.kisi@gmail.com, herkes yazsın")]
    [InlineData("İletişim: kisi_42@firma.com.tr")]
    public void Analyze_HoldsEmailAddressForReview(string text)
    {
        var decision = _service.Analyze(text);

        decision.Status.Should().Be("under_review");
    }

    [Theory]
    [InlineData("Arkadaşım 5 yıl önce 300 lira borç aldı, hâlâ ödemedi.")]
    [InlineData("Toplantı 15:30'da başlıyor, 2 saat sürecek.")]
    public void Analyze_KeepsOrdinaryNumbersActive(string text)
    {
        _service.Analyze(text).Status.Should().Be("active");
    }
}
