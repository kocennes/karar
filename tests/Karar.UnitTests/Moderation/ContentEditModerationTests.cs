using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

// PUT /api/v1/posts/{id} ve PUT /api/v1/comments/{id} düzenleme kararı: oluşturma ile aynı
// ContentModerationService + Perspective eşikleri. Uç nokta bu karara göre 400 döner ya da yazar.
public sealed class ContentEditModerationTests
{
    private readonly ContentModerationService _moderation = new();

    [Fact]
    public void BenignEdit_IsAllowed()
    {
        var decision = Decide("Arkadaşım doğum günümü unuttu, kırılmakta haklı mıyım sizce?");

        decision.Outcome.Should().Be(ContentEditOutcome.Allowed);
        decision.ErrorCode.Should().BeNull();
    }

    [Theory]
    [InlineData("Bu kişinin 34567890123 TC'si paylaşıldı")]
    [InlineData("Havale için TR330006100519786457841326 kullan")]
    [InlineData("Kartım: 4111 1111 1111 1111")]
    [InlineData("seni öldüreceğim")]
    public void DoxxingOrThreatEdit_IsRejectedLikeCreate(string editedText)
    {
        var decision = Decide(editedText);

        decision.Outcome.Should().Be(ContentEditOutcome.Rejected);
        decision.ErrorCode.Should().Be("CONTENT_REJECTED");
    }

    [Theory]
    [InlineData("Adresimi herkes bilsin, kapıma gelip konuşsun bakalım.")]
    [InlineData("Bana 0532 123 45 67 numarasından ulaşıp tehdit etti.")]
    public void ReviewLevelEdit_IsNotSaved(string editedText)
    {
        _moderation.Analyze(editedText).Status.Should().Be("under_review",
            "precondition: create would hold this text for moderator review");

        var decision = Decide(editedText);

        decision.Outcome.Should().Be(ContentEditOutcome.RequiresReview,
            "an edit must not publish text that create would hold for review");
        decision.ErrorCode.Should().Be(ContentEditModeration.RequiresReviewCode);
        decision.IsCrisisFlagged.Should().BeFalse();
    }

    [Fact]
    public void CrisisEdit_IsNotSavedAndKeepsCrisisSignal()
    {
        var decision = Decide("Artık yaşamak istemiyorum, hayatıma son vermek istiyorum.");

        decision.Outcome.Should().Be(ContentEditOutcome.RequiresReview);
        decision.IsCrisisFlagged.Should().BeTrue("the supportive crisis notification must still be sent");
    }

    [Fact]
    public void PerspectiveSevereToxicity_IsRejected()
    {
        var decision = ContentEditModeration.Decide(
            _moderation.Analyze("Tamamen zararsız görünen bir metin, burada bir şey yok."),
            new PerspectiveResult(0.95f, 0.9f, 0.1f, 0.8f, 0.1f, 0.95f));

        decision.Outcome.Should().Be(ContentEditOutcome.Rejected);
    }

    [Fact]
    public void PerspectiveReviewBand_IsNotSaved()
    {
        var decision = ContentEditModeration.Decide(
            _moderation.Analyze("Tamamen zararsız görünen bir metin, burada bir şey yok."),
            new PerspectiveResult(0.75f, 0.2f, 0.1f, 0.6f, 0.1f, 0.75f));

        decision.Outcome.Should().Be(ContentEditOutcome.RequiresReview);
    }

    [Fact]
    public void PerspectiveUnavailable_FallsBackToKeywordModeration()
    {
        var decision = ContentEditModeration.Decide(
            _moderation.Analyze("Tamamen zararsız görünen bir metin, burada bir şey yok."),
            perspective: null);

        decision.Outcome.Should().Be(ContentEditOutcome.Allowed);
    }

    private ContentEditDecision Decide(string text) =>
        ContentEditModeration.Decide(_moderation.Analyze(text));
}
