using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Notifications;

public sealed class NotificationDispatcherTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(4, 8)]
    [InlineData(5, 16)]
    [InlineData(20, 64)]
    public void CalculateRetryDelay_UsesCappedExponentialBackoff(int attemptCount, int expectedMinutes)
    {
        NotificationDispatcher.CalculateRetryDelay(attemptCount)
            .Should()
            .Be(TimeSpan.FromMinutes(expectedMinutes));
    }
}
