using FluentAssertions;
using FirebaseAdmin.Messaging;
using Karar.Api.Services;

namespace Karar.UnitTests.Notifications;

public sealed class NotificationDispatcherTests
{
    // ─── CalculateBaseDelay ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 60)]
    [InlineData(2, 120)]
    [InlineData(3, 240)]
    [InlineData(4, 480)]
    [InlineData(5, 960)]
    [InlineData(6, 1920)]
    [InlineData(7, 3600)] // capped at max_delay
    [InlineData(20, 3600)] // stays capped
    public void CalculateBaseDelay_UsesCappedExponentialBackoffInSeconds(int attempt, int expectedSeconds)
    {
        NotificationDispatcher.CalculateBaseDelay(attempt)
            .Should()
            .Be(TimeSpan.FromSeconds(expectedSeconds));
    }

    // ─── CalculateRetryDelay ─────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(7)]
    public void CalculateRetryDelay_IsBaseDelayPlusUpToTenPercentJitter(int attempt)
    {
        var baseDelay = NotificationDispatcher.CalculateBaseDelay(attempt);
        var maxDelay = baseDelay + TimeSpan.FromSeconds(baseDelay.TotalSeconds * 0.1);

        var retryDelay = NotificationDispatcher.CalculateRetryDelay(attempt);

        retryDelay.Should().BeGreaterThanOrEqualTo(baseDelay);
        retryDelay.Should().BeLessThanOrEqualTo(maxDelay);
    }

    // ─── DetermineDeliveryDecision: sent_at behavior ─────────────────────────

    [Fact]
    public void DetermineDeliveryDecision_Success_ReturnsMarkSent_AndSetsProviderMessageId()
    {
        var result = NotificationDispatcher.PushSendResult.Success("msg-abc-123");

        var decision = NotificationDispatcher.DetermineDeliveryDecision(result, 0, 5);

        decision.Action.Should().Be(NotificationDispatcher.DeliveryAction.MarkSent);
        decision.ProviderMessageId.Should().Be("msg-abc-123");
        decision.Error.Should().BeNull();
        decision.RetryDelay.Should().BeNull();
    }

    [Fact]
    public void DetermineDeliveryDecision_PermanentFailure_ReturnsMarkFailed_NoProviderMessageId()
    {
        var result = NotificationDispatcher.PushSendResult.PermanentFailure("all_tokens_unregistered");

        var decision = NotificationDispatcher.DetermineDeliveryDecision(result, 0, 5);

        // sent_at must NOT be set for MarkFailed
        decision.Action.Should().Be(NotificationDispatcher.DeliveryAction.MarkFailed);
        decision.ProviderMessageId.Should().BeNull();
        decision.Error.Should().Be("all_tokens_unregistered");
        decision.RetryDelay.Should().BeNull();
    }

    [Fact]
    public void DetermineDeliveryDecision_PermanentFailure_AtAnyAttemptCount_StillMarkFailed()
    {
        var result = NotificationDispatcher.PushSendResult.PermanentFailure("no_fcm_token");

        // Even on first attempt, permanent failure goes straight to MarkFailed
        var decision = NotificationDispatcher.DetermineDeliveryDecision(result, 0, 5);

        decision.Action.Should().Be(NotificationDispatcher.DeliveryAction.MarkFailed);
        decision.ProviderMessageId.Should().BeNull();
    }

    [Fact]
    public void DetermineDeliveryDecision_TransientFailure_BelowMaxAttempts_ReturnsScheduleRetry_NoProviderMessageId()
    {
        var result = NotificationDispatcher.PushSendResult.TransientFailure("fcm_send_failed");

        var decision = NotificationDispatcher.DetermineDeliveryDecision(result, 1, 5);

        // sent_at must NOT be set for ScheduleRetry
        decision.Action.Should().Be(NotificationDispatcher.DeliveryAction.ScheduleRetry);
        decision.ProviderMessageId.Should().BeNull();
        decision.Error.Should().Be("fcm_send_failed");
        decision.RetryDelay.Should().NotBeNull();
    }

    [Fact]
    public void DetermineDeliveryDecision_TransientFailure_AtMaxAttempts_ReturnsMarkFailed()
    {
        var result = NotificationDispatcher.PushSendResult.TransientFailure("timeout");

        // attemptCount + 1 >= maxAttempts → give up
        var decision = NotificationDispatcher.DetermineDeliveryDecision(result, 4, 5);

        decision.Action.Should().Be(NotificationDispatcher.DeliveryAction.MarkFailed);
        decision.ProviderMessageId.Should().BeNull();
        decision.Error.Should().Be("timeout");
        decision.RetryDelay.Should().BeNull();
    }

    [Fact]
    public void DetermineDeliveryDecision_TransientFailure_ExceedsMaxAttempts_ReturnsMarkFailed()
    {
        var result = NotificationDispatcher.PushSendResult.TransientFailure("err");

        var decision = NotificationDispatcher.DetermineDeliveryDecision(result, 10, 5);

        decision.Action.Should().Be(NotificationDispatcher.DeliveryAction.MarkFailed);
    }

    [Theory]
    [InlineData(MessagingErrorCode.Unregistered, true)]
    [InlineData(MessagingErrorCode.SenderIdMismatch, true)]
    [InlineData(MessagingErrorCode.Unavailable, false)]
    [InlineData(MessagingErrorCode.Internal, false)]
    [InlineData(MessagingErrorCode.QuotaExceeded, false)]
    public void IsPermanentTokenFailure_OnlyTreatsNonRecoverableTokenErrorsAsCleanupCandidates(
        MessagingErrorCode errorCode,
        bool expected)
    {
        NotificationDispatcher.IsPermanentTokenFailure(errorCode).Should().Be(expected);
    }

    // ─── DetermineDeliveryDecision: retry delay propagation ──────────────────

    [Theory]
    [InlineData(0)]   // next attempt 1 → base 60 s
    [InlineData(1)]   // next attempt 2 → base 120 s
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void DetermineDeliveryDecision_TransientFailure_RetryDelayMatchesCalculateRetryDelay(int attemptCount)
    {
        var result = NotificationDispatcher.PushSendResult.TransientFailure("err");
        var baseDelay = NotificationDispatcher.CalculateBaseDelay(attemptCount + 1);
        var maxDelay = baseDelay + TimeSpan.FromSeconds(baseDelay.TotalSeconds * 0.1);

        var decision = NotificationDispatcher.DetermineDeliveryDecision(result, attemptCount, 10);

        decision.Action.Should().Be(NotificationDispatcher.DeliveryAction.ScheduleRetry);
        decision.RetryDelay.Should().BeGreaterThanOrEqualTo(baseDelay);
        decision.RetryDelay.Should().BeLessThanOrEqualTo(maxDelay);
    }

    // ─── Channel / category / deeplink helpers ───────────────────────────────

    [Theory]
    [InlineData("comment_on_post", "comments")]
    [InlineData("reply_on_comment", "mentions")]
    [InlineData("mention", "mentions")]
    [InlineData("follow", "mentions")]
    [InlineData("verdict_milestone", "milestones")]
    [InlineData("viral_post_owner", "milestones")]
    [InlineData("trend_alert", "viral")]
    [InlineData("follow_new_post", "viral")]
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
    [InlineData("follow", "FOLLOW")]
    [InlineData("verdict_milestone", "MILESTONE")]
    [InlineData("viral_post_owner", "MILESTONE")]
    [InlineData("trend_alert", "TREND")]
    [InlineData("follow_new_post", "TREND")]
    [InlineData("weekly_digest", "DIGEST")]
    [InlineData("moderation_result", "MODERATION")]
    [InlineData("system_announcement", "SYSTEM")]
    public void GetApnsCategory_MapsAllKnownTypes(string type, string expectedCategory)
    {
        NotificationDispatcher.GetApnsCategory(type).Should().Be(expectedCategory);
    }

    [Theory]
    [InlineData("comment_on_post", "11111111-1111-1111-1111-111111111111", "/posts/11111111-1111-1111-1111-111111111111")]
    [InlineData("verdict_milestone", "22222222-2222-2222-2222-222222222222", "/posts/22222222-2222-2222-2222-222222222222")]
    [InlineData("trend_alert", "33333333-3333-3333-3333-333333333333", "/posts/33333333-3333-3333-3333-333333333333")]
    [InlineData("follow_new_post", "44444444-4444-4444-4444-444444444444", "/posts/44444444-4444-4444-4444-444444444444")]
    [InlineData("weekly_digest", null, "/notifications")]
    [InlineData("moderation_result", null, "/profile")]
    [InlineData("system_announcement", null, "/notifications")]
    public void BuildDeepLink_ReturnsCorrectPath(string type, string? postIdStr, string expectedPath)
    {
        var postId = postIdStr is null ? (Guid?)null : Guid.Parse(postIdStr);
        NotificationDispatcher.BuildDeepLink(type, postId).Should().Be(expectedPath);
    }
}
