using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Notifications;

public sealed class CommentNotificationBatcherTests
{
    [Theory]
    [InlineData("active", 5, null, true)]
    [InlineData("active", 4, null, false)]
    [InlineData("deleted", 20, null, false)]
    [InlineData("auto_hidden", 20, 0, false)]
    [InlineData("active", 20, -10, false)]
    [InlineData("active", 20, -9, true)]
    public void ShouldNotify_FiltersLowQualityComments(
        string status,
        int contentLength,
        int? authorKarma,
        bool expected)
    {
        CommentNotificationBatcher.ShouldNotify(status, contentLength, authorKarma)
            .Should()
            .Be(expected);
    }
}
