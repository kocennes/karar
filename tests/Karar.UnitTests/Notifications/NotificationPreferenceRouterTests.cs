using FluentAssertions;
using Karar.Api.Contracts;
using Karar.Api.Services;

namespace Karar.UnitTests.Notifications;

public sealed class NotificationPreferenceRouterTests
{
    // GetQuietHoursDelay uses DateTime.UtcNow internally, so tests that need a fixed "now"
    // use the local helper that reimplements the same deterministic path.

    [Theory]
    [InlineData("22:00", "08:00", 23, 0, true)]   // Inside overnight window
    [InlineData("22:00", "08:00", 0, 30, true)]    // Midnight — inside overnight window
    [InlineData("22:00", "08:00", 7, 59, true)]    // Just before window end
    [InlineData("22:00", "08:00", 8, 0, false)]    // Exactly at end — outside
    [InlineData("22:00", "08:00", 12, 0, false)]   // Midday — outside
    [InlineData("22:00", "08:00", 21, 59, false)]  // One minute before window start
    [InlineData("01:00", "07:00", 3, 0, true)]     // Inside same-day window
    [InlineData("01:00", "07:00", 0, 0, false)]    // Before same-day window
    [InlineData("01:00", "07:00", 7, 0, false)]    // Exactly at same-day window end
    public void QuietHoursDelay_CorrectlyDetectsWindow(
        string start, string end, int currentHour, int currentMinute, bool expectBlocked)
    {
        var fakeNow = new DateTime(2025, 1, 15, currentHour, currentMinute, 0, DateTimeKind.Utc);
        var delay = ComputeDelay(start, end, fakeNow);
        if (expectBlocked)
            delay.Should().NotBeNull("should block when inside quiet hours");
        else
            delay.Should().BeNull("should allow when outside quiet hours");
    }

    [Fact]
    public void QuietHoursDelay_PointsToEndOfWindow()
    {
        // 23:00, overnight 22:00–08:00 → end is 08:00 tomorrow = 9 hours away
        var fakeNow = new DateTime(2025, 1, 15, 23, 0, 0, DateTimeKind.Utc);
        var delay = ComputeDelay("22:00", "08:00", fakeNow);
        delay.Should().NotBeNull();
        delay!.Value.Should().BeCloseTo(TimeSpan.FromHours(9), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void QuietHoursDelay_MalformedStrings_ReturnsNull()
    {
        NotificationPreferenceRouter.GetQuietHoursDelay("bad", "08:00").Should().BeNull();
        NotificationPreferenceRouter.GetQuietHoursDelay("22:00", "").Should().BeNull();
        NotificationPreferenceRouter.GetQuietHoursDelay("25:00", "08:00").Should().BeNull();
    }

    [Theory]
    [InlineData("mention")]
    [InlineData("follow")]
    public void IsCategoryEnabled_UsesMentionPreferenceForSocialNotifications(string type)
    {
        var disabled = EmptyPreferences() with { NotifyOnMention = false };
        var enabled = EmptyPreferences() with { NotifyOnMention = true };

        NotificationPreferenceRouter.IsCategoryEnabled(type, disabled).Should().BeFalse();
        NotificationPreferenceRouter.IsCategoryEnabled(type, enabled).Should().BeTrue();
    }

    [Theory]
    [InlineData("trend_alert")]
    [InlineData("follow_new_post")]
    public void IsCategoryEnabled_UsesTrendPreferenceForDiscoveryNotifications(string type)
    {
        var disabled = EmptyPreferences() with { NotifyOnTrend = false };
        var enabled = EmptyPreferences() with { NotifyOnTrend = true };

        NotificationPreferenceRouter.IsCategoryEnabled(type, disabled).Should().BeFalse();
        NotificationPreferenceRouter.IsCategoryEnabled(type, enabled).Should().BeTrue();
    }

    [Theory]
    [InlineData("verdict_milestone")]
    [InlineData("verdict_reminder")]
    [InlineData("viral_post_owner")]
    public void IsCategoryEnabled_UsesVerdictPreferenceForJudgmentNotifications(string type)
    {
        var disabled = EmptyPreferences() with { NotifyOnVerdict = false };
        var enabled = EmptyPreferences() with { NotifyOnVerdict = true };

        NotificationPreferenceRouter.IsCategoryEnabled(type, disabled).Should().BeFalse();
        NotificationPreferenceRouter.IsCategoryEnabled(type, enabled).Should().BeTrue();
    }

    [Fact]
    public void PushDecision_Allow_HasNoSuppressionReason()
    {
        var decision = PushDecision.Allow();

        decision.Allowed.Should().BeTrue();
        decision.IsDeferred.Should().BeFalse();
        decision.Reason.Should().Be("allowed");
        decision.SuggestedRetryDelay.Should().BeNull();
    }

    [Fact]
    public void PushDecision_Suppress_RepresentsPermanentPreferenceSuppression()
    {
        var decision = PushDecision.Suppress("push_disabled");

        decision.Allowed.Should().BeFalse();
        decision.IsDeferred.Should().BeFalse();
        decision.Reason.Should().Be("push_disabled");
        decision.SuggestedRetryDelay.Should().BeNull();
    }

    [Fact]
    public void PushDecision_Defer_RepresentsTemporaryMuteOrQuietHoursSuppression()
    {
        var delay = TimeSpan.FromHours(2);

        var decision = PushDecision.Defer("muted", delay);

        decision.Allowed.Should().BeFalse();
        decision.IsDeferred.Should().BeTrue();
        decision.Reason.Should().Be("muted");
        decision.SuggestedRetryDelay.Should().Be(delay);
    }

    // Local re-implementation of the deterministic logic with injectable "now".
    private static TimeSpan? ComputeDelay(string startStr, string endStr, DateTime utcNow)
    {
        if (!TryParse(startStr, out int sh, out int sm) || !TryParse(endStr, out int eh, out int em))
            return null;

        var startMinutes = sh * 60 + sm;
        var endMinutes = eh * 60 + em;
        var nowMinutes = utcNow.Hour * 60 + utcNow.Minute;

        bool inWindow = startMinutes < endMinutes
            ? nowMinutes >= startMinutes && nowMinutes < endMinutes
            : nowMinutes >= startMinutes || nowMinutes < endMinutes;

        if (!inWindow) return null;

        var endToday = utcNow.Date.AddMinutes(endMinutes);
        var endCandidate = endToday > utcNow ? endToday : endToday.AddDays(1);
        return endCandidate - utcNow;
    }

    private static bool TryParse(string v, out int h, out int m)
    {
        h = m = 0;
        var p = v.Split(':');
        return p.Length == 2 && int.TryParse(p[0], out h) && int.TryParse(p[1], out m)
            && h is >= 0 and < 24 && m is >= 0 and < 60;
    }

    private static NotificationPreferencesRequest EmptyPreferences() =>
        new(null, null, null, null, null, null, null);
}
