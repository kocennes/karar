using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Notifications;

public sealed class NotificationRateLimiterTests
{
    [Theory]
    [InlineData("2026-05-20T04:59:00Z", true)]
    [InlineData("2026-05-20T05:00:00Z", false)]
    [InlineData("2026-05-20T19:59:00Z", false)]
    [InlineData("2026-05-20T20:00:00Z", true)]
    public void IsQuietHour_UsesTurkeyLocalTime(string utc, bool expected)
    {
        NotificationRateLimiter.IsQuietHour(DateTimeOffset.Parse(utc)).Should().Be(expected);
    }

    [Theory]
    [InlineData("2026-05-20T17:59:00Z", true)]
    [InlineData("2026-05-20T18:00:00Z", false)]
    [InlineData("2026-05-20T21:30:00Z", false)]
    [InlineData("2026-05-20T21:31:00Z", true)]
    [InlineData("2026-05-20T05:00:00Z", true)]
    public void IsQuietHour_UsesRamadanWindowWhenEnabled(string utc, bool expected)
    {
        NotificationRateLimiter.IsQuietHour(DateTimeOffset.Parse(utc), ramadanMode: true)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void GetDailyLimit_ReturnsOneForFirstFortyEightHours()
    {
        var now = DateTimeOffset.Parse("2026-05-20T12:00:00Z");
        var createdAt = now.AddHours(-47);

        NotificationRateLimiter.GetDailyLimit(createdAt, now).Should().Be(1);
    }

    [Fact]
    public void GetDailyLimit_ReturnsTwoAfterFortyEightHours()
    {
        var now = DateTimeOffset.Parse("2026-05-20T12:00:00Z");
        var createdAt = now.AddHours(-48);

        NotificationRateLimiter.GetDailyLimit(createdAt, now).Should().Be(2);
    }

    [Theory]
    [InlineData("moderation_result", NotificationPriority.Critical)]
    [InlineData("verdict_milestone", NotificationPriority.High)]
    [InlineData("comment_on_post", NotificationPriority.Normal)]
    public void GetPriority_MapsNotificationTypes(string type, NotificationPriority expected)
    {
        NotificationRateLimiter.GetPriority(type).Should().Be(expected);
    }
}
