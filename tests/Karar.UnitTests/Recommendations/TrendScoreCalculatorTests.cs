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

    [Fact]
    public void Score_DropsSignificantly_WhenReportRateIsHigh()
    {
        var noReports = TrendScoreCalculator.Compute(
            hakliVotes: 50,
            haksizVotes: 50,
            comments: 10,
            ageHours: 1,
            exposures: 1000
        );

        var manyReports = TrendScoreCalculator.Compute(
            hakliVotes: 50,
            haksizVotes: 50,
            comments: 10,
            ageHours: 1,
            exposures: 1000,
            pendingReports: 50 // 5% report rate
        );

        manyReports.Should().BeLessThan(noReports * 0.6);
    }

    [Fact]
    public void Score_DropsToNearZero_WhenToxicityIsVeryHigh()
    {
        var toxicScore = TrendScoreCalculator.Compute(
            hakliVotes: 100,
            haksizVotes: 100,
            comments: 50,
            ageHours: 1,
            perspectiveToxicity: 0.8
        );

        toxicScore.Should().BeLessThan(1.0);
    }

    [Fact]
    public void Score_RewardsBalancedDiscussions()
    {
        var biased = TrendScoreCalculator.Compute(
            hakliVotes: 90,
            haksizVotes: 10,
            comments: 10,
            ageHours: 1
        );

        var balanced = TrendScoreCalculator.Compute(
            hakliVotes: 50,
            haksizVotes: 50,
            comments: 10,
            ageHours: 1
        );

        balanced.Should().BeGreaterThan(biased);
    }

    [Fact]
    public void Score_IncreasesWithUniqueActorsButLowExposureDoesNotExplode()
    {
        var smallSample = TrendScoreCalculator.Compute(
            hakliVotes: 2,
            haksizVotes: 1,
            comments: 1,
            ageHours: 1,
            exposures: 3
        );

        var broaderSample = TrendScoreCalculator.Compute(
            hakliVotes: 20,
            haksizVotes: 10,
            comments: 1,
            ageHours: 1,
            exposures: 300
        );

        broaderSample.Should().BeGreaterThan(smallSample);
        smallSample.Should().BeLessThan(10);
    }

    [Fact]
    public void Score_RewardsQualityComments()
    {
        var shallowDiscussion = TrendScoreCalculator.Compute(
            hakliVotes: 40,
            haksizVotes: 35,
            comments: 10,
            ageHours: 2,
            qualityComments: 0
        );

        var qualityDiscussion = TrendScoreCalculator.Compute(
            hakliVotes: 40,
            haksizVotes: 35,
            comments: 10,
            ageHours: 2,
            qualityComments: 6
        );

        qualityDiscussion.Should().BeGreaterThan(shallowDiscussion);
    }

    [Fact]
    public void BalancedBonus_DoesNotOverrideSafetyPenalty()
    {
        var balancedSafe = TrendScoreCalculator.Compute(
            hakliVotes: 50,
            haksizVotes: 50,
            comments: 10,
            ageHours: 1,
            perspectiveToxicity: 0.1
        );

        var balancedToxic = TrendScoreCalculator.Compute(
            hakliVotes: 50,
            haksizVotes: 50,
            comments: 10,
            ageHours: 1,
            perspectiveToxicity: 0.6
        );

        balancedToxic.Should().BeLessThan(balancedSafe * 0.5);
    }
}
