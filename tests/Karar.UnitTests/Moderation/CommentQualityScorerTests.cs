using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

public sealed class CommentQualityScorerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Score_EmptyOrWhitespace_ReturnsZeroPenalty(string content)
    {
        CommentQualityScorer.Score(content).Should().Be(0f);
    }

    [Fact]
    public void Score_SingleWordInsult_ReturnsPenalty()
    {
        CommentQualityScorer.Score("salak").Should().BeGreaterThanOrEqualTo(0.4f);
    }

    [Fact]
    public void Score_NormalTurkishSentence_ReturnsZeroPenalty()
    {
        var content = "Bu yorum konuya sakin ve dengeli bir katki sunuyor.";

        CommentQualityScorer.Score(content).Should().Be(0f);
    }

    [Fact]
    public void Score_EmojiOnly_ReturnsPenalty()
    {
        CommentQualityScorer.Score("🔥🔥🔥🔥").Should().BeGreaterThanOrEqualTo(0.5f);
    }

    [Fact]
    public void Score_AllCapsWithThreeOrMoreWords_IncludesCapsPenalty()
    {
        CommentQualityScorer.Score("BU GERCEKTEN KOTU").Should().BeGreaterThanOrEqualTo(0.2f);
    }
}
