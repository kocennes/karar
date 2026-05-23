using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Recommendations;

public sealed class UcbScoreCalculatorTests
{
    [Fact]
    public void Score_RewardsHighConversion_WhenExposureIsComparable()
    {
        var strongPost = UcbScoreCalculator.Compute(rewards: 8, exposures: 20, totalExposures: 100);
        var weakPost = UcbScoreCalculator.Compute(rewards: 2, exposures: 20, totalExposures: 100);

        strongPost.Should().BeGreaterThan(weakPost);
    }

    [Fact]
    public void Score_PreservesExploration_ForLowExposurePost()
    {
        var uncertainPost = UcbScoreCalculator.Compute(rewards: 1, exposures: 1, totalExposures: 100);
        var knownAveragePost = UcbScoreCalculator.Compute(rewards: 10, exposures: 100, totalExposures: 100);

        uncertainPost.Should().BeGreaterThan(knownAveragePost);
    }

    [Fact]
    public void Score_ClampsInvalidInputs()
    {
        var score = UcbScoreCalculator.Compute(rewards: -5, exposures: 0, totalExposures: 0);

        score.Should().BeApproximately(Math.Sqrt(2.0 * Math.Log(2.0)), 0.000001);
    }
}
