using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Notifications;

public sealed class VerdictReminderJobTests
{
    [Theory]
    [InlineData(5, 0, 100)]
    [InlineData(3, 2, 60)]
    [InlineData(1, 2, 33)]
    [InlineData(0, 0, 0)]
    public void CalculateHakliPercent_ReturnsRoundedPercentage(int hakli, int haksiz, int expected)
    {
        VerdictReminderJob.CalculateHakliPercent(hakli, haksiz).Should().Be(expected);
    }
}
