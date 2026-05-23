using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Recommendations;

public sealed class TrendScoreCalculatorTests
{
    [Fact]
    public void Score_IsHigher_ForNewPostWithManyVotes()
    {
        var newPopular = TrendScoreCalculator.Compute(
            hakliVotes: 120,
            haksizVotes: 80,
            comments: 40,
            ageHours: 1
        );
        var oldPopular = TrendScoreCalculator.Compute(
            hakliVotes: 300,
            haksizVotes: 200,
            comments: 100,
            ageHours: 48
        );

        newPopular.Should().BeGreaterThan(oldPopular);
    }

    [Fact]
    public void Score_IsZero_ForZeroEngagement()
    {
        var score = TrendScoreCalculator.Compute(
            hakliVotes: 0,
            haksizVotes: 0,
            comments: 0,
            ageHours: 1
        );

        score.Should().Be(0);
    }

    [Fact]
    public void Score_NeverGoesNegative()
    {
        var score = TrendScoreCalculator.Compute(
            hakliVotes: -10,
            haksizVotes: -10,
            comments: -5,
            ageHours: -1
        );

        score.Should().Be(0);
    }

    [Fact]
    public void Score_UsesDwellTimeAsBoundedWeight()
    {
        var lowDwell = TrendScoreCalculator.Compute(
            hakliVotes: 20,
            haksizVotes: 10,
            comments: 5,
            ageHours: 2,
            averageDwellSeconds: 0
        );
        var healthyDwell = TrendScoreCalculator.Compute(
            hakliVotes: 20,
            haksizVotes: 10,
            comments: 5,
            ageHours: 2,
            averageDwellSeconds: 30
        );

        healthyDwell.Should().BeGreaterThan(lowDwell);
        healthyDwell.Should().BeApproximately(lowDwell / 0.8, 0.000001);
    }

    [Fact]
    public void Score_CapsDwellBoost()
    {
        var longDwell = TrendScoreCalculator.Compute(
            hakliVotes: 20,
            haksizVotes: 10,
            comments: 5,
            ageHours: 2,
            averageDwellSeconds: 45
        );
        var extremeDwell = TrendScoreCalculator.Compute(
            hakliVotes: 20,
            haksizVotes: 10,
            comments: 5,
            ageHours: 2,
            averageDwellSeconds: 900
        );

        extremeDwell.Should().BeApproximately(longDwell, 0.000001);
    }
}
