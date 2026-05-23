using FluentAssertions;
using Karar.Api.Common;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

public sealed class ModerationTextNormalizerTests
{
    [Fact]
    public void NormalizeForModeration_RemovesZeroWidthCharacters()
    {
        var result = ModerationTextNormalizer.NormalizeForModeration("t\u200bc\u200b kimlik");

        result.Should().Be("tc kimlik");
    }

    [Fact]
    public void NormalizeForModeration_MapsCommonHomoglyphs()
    {
        var result = ModerationTextNormalizer.NormalizeForModeration("аdrеsim |stanbul");

        result.Should().Be("adresim lstanbul");
    }

    [Fact]
    public void Analyze_ReviewsPolicyTermsHiddenWithZeroWidthCharacters()
    {
        var service = new ContentModerationService();

        var result = service.Analyze("T\u200bC kimlik numaramı paylaşayım mı?");

        result.Status.Should().Be("under_review");
    }
}
