using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Metrics;

public sealed class TrendGrowthCalculatorTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(5, 0, 999)]
    [InlineData(10, 10, 0)]
    [InlineData(15, 10, 50)]
    [InlineData(5, 10, -50)]
    [InlineData(0, 10, -100)]
    [InlineData(1000, 10, 999)]
    [InlineData(3, 2, 50)]
    [InlineData(7, 3, 133)]
    public void Percent_ComputesSignedCappedChange(int current, int previous, int expected)
    {
        TrendGrowthCalculator.Percent(current, previous).Should().Be(expected);
    }

    [Fact]
    public void Percent_NeverExceedsCap_AndNeverBelowMinusHundred()
    {
        TrendGrowthCalculator.Percent(int.MaxValue, 1).Should().Be(TrendGrowthCalculator.MaxPercent);
        TrendGrowthCalculator.Percent(0, int.MaxValue).Should().Be(-100);
    }

    [Fact]
    public void Percent_TreatsNegativeInputsAsZero()
    {
        TrendGrowthCalculator.Percent(-4, -2).Should().Be(0);
        TrendGrowthCalculator.Percent(4, -2).Should().Be(TrendGrowthCalculator.MaxPercent);
    }
}
