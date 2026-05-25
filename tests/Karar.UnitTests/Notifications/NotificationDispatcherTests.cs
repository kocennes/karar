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

    [Theory]
    [InlineData("comment_on_post", "comments")]
    [InlineData("reply_on_comment", "mentions")]
    [InlineData("mention", "mentions")]
    [InlineData("verdict_milestone", "milestones")]
    [InlineData("viral_post_owner", "milestones")]
    [InlineData("trend_alert", "viral")]
    [InlineData("weekly_digest", "digest")]
    [InlineData("moderation_result", "system")]
    [InlineData("system_announcement", "system")]
    public void GetAndroidChannelId_MapsAllKnownTypes(string type, string expectedChannel)
    {
        NotificationDispatcher.GetAndroidChannelId(type).Should().Be(expectedChannel);
    }

    [Theory]
    [InlineData("comment_on_post", "COMMENT")]
    [InlineData("reply_on_comment", "REPLY")]
    [InlineData("mention", "MENTION")]
    [InlineData("verdict_milestone", "MILESTONE")]
    [InlineData("viral_post_owner", "MILESTONE")]
    [InlineData("moderation_result", "MODERATION")]
    [InlineData("system_announcement", "SYSTEM")]
    public void GetApnsCategory_MapsAllKnownTypes(string type, string expectedCategory)
    {
        NotificationDispatcher.GetApnsCategory(type).Should().Be(expectedCategory);
    }

    [Theory]
    [InlineData("comment_on_post", "11111111-1111-1111-1111-111111111111", "/posts/11111111-1111-1111-1111-111111111111")]
    [InlineData("verdict_milestone", "22222222-2222-2222-2222-222222222222", "/posts/22222222-2222-2222-2222-222222222222")]
    [InlineData("weekly_digest", null, "/notifications")]
    [InlineData("moderation_result", null, "/profile")]
    [InlineData("system_announcement", null, "/notifications")]
    public void BuildDeepLink_ReturnsCorrectPath(string type, string? postIdStr, string expectedPath)
    {
        var postId = postIdStr is null ? (Guid?)null : Guid.Parse(postIdStr);
        NotificationDispatcher.BuildDeepLink(type, postId).Should().Be(expectedPath);
    }
}
