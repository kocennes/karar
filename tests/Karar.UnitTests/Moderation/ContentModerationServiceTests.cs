using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

public sealed class ContentModerationServiceTests
{
    private readonly ContentModerationService _service = new();

    [Fact]
    public void Analyze_ReturnsActive_ForCleanConflict()
    {
        var result = _service.Analyze(
            "Arkadaşım borcunu ödemeden telefon aldı, bunu hatırlatmakta haklı mıyım?"
        );

        result.Status.Should().Be("active");
        result.IsRejected.Should().BeFalse();
    }

    [Fact]
    public void Analyze_ReturnsUnderReview_ForPhoneNumber()
    {
        var result = _service.Analyze(
            "Bana 0532 123 45 67 numarasından ulaşıp tehdit etti."
        );

        result.Status.Should().Be("under_review");
        result.IsRejected.Should().BeFalse();
    }

    [Fact]
    public void Analyze_RejectsForbiddenPolicyContent()
    {
        var result = _service.Analyze("Bomba yapımı hakkında detaylı bilgi istiyorum.");

        result.Status.Should().Be("rejected");
        result.IsRejected.Should().BeTrue();
        result.Code.Should().Be("CONTENT_REJECTED");
    }
}
