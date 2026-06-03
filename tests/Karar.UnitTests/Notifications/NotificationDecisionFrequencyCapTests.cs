using FluentAssertions;

namespace Karar.UnitTests.Notifications;

public sealed class NotificationDecisionFrequencyCapTests
{
    [Fact]
    public void DecisionService_AppliesDailyHourlyFrequencyCapBeforeSlidingWindow()
    {
        var servicePath = FindRepoFile("backend/Karar.Api/Services/NotificationDecisionService.cs");
        var service = File.ReadAllText(servicePath);

        var frequencyCapIndex = service.IndexOf("rateLimiter.CanSendAsync", StringComparison.Ordinal);
        var slidingWindowIndex = service.IndexOf("rateLimiter.CheckSlidingWindowAsync", StringComparison.Ordinal);

        frequencyCapIndex.Should().BeGreaterThanOrEqualTo(0,
            "daily/hourly notification caps must be part of the production push decision");
        slidingWindowIndex.Should().BeGreaterThan(frequencyCapIndex,
            "coarse frequency caps should run before the short sliding-window limiter");
        service.Should().Contain("deviceCreatedAt");
        service.Should().Contain("\"frequency_cap\"");
    }

    [Fact]
    public void Dispatcher_PassesDeviceCreatedAtIntoNotificationDecision()
    {
        var dispatcherPath = FindRepoFile("backend/Karar.Api/Services/NotificationDispatcher.cs");
        var dispatcher = File.ReadAllText(dispatcherPath);

        var callStart = dispatcher.IndexOf("decisionService.EvaluateAsync(", StringComparison.Ordinal);
        callStart.Should().BeGreaterThanOrEqualTo(0);
        var callEnd = dispatcher.IndexOf("connection,", callStart, StringComparison.Ordinal);
        callEnd.Should().BeGreaterThan(callStart);

        dispatcher[callStart..callEnd].Should().Contain("notification.DeviceCreatedAt");
    }

    [Fact]
    public void DecisionService_PropagatesPreferenceSuppressionReason()
    {
        var servicePath = FindRepoFile("backend/Karar.Api/Services/NotificationDecisionService.cs");
        var service = File.ReadAllText(servicePath);

        var preferenceBlock = Slice(
            service,
            "if (!preferenceDecision.Allowed)",
            "// 3. Daily/hourly frequency cap");

        preferenceBlock.Should().Contain("preferenceDecision.Reason");
        preferenceBlock.Should().Contain("preferenceDecision.IsDeferred");
        preferenceBlock.Should().Contain("NotificationDecision.Defer(reason");
        preferenceBlock.Should().Contain("NotificationDecision.Suppress(reason)");
        preferenceBlock.Should().Contain("\"suppressed\"");
    }

    [Fact]
    public void Dispatcher_DeferPathPreservesInAppNotificationRows()
    {
        var dispatcherPath = FindRepoFile("backend/Karar.Api/Services/NotificationDispatcher.cs");
        var dispatcher = File.ReadAllText(dispatcherPath);

        var deferBlock = Slice(
            dispatcher,
            "private static async Task DeferNotificationsAsync",
            "private async Task UpdateDeliveryStateAsync");

        deferBlock.Should().Contain("UPDATE notifications");
        deferBlock.Should().Contain("next_attempt_at = NOW()");
        deferBlock.Should().Contain("last_error = 'rate_limited'");
        deferBlock.Should().NotContain("DELETE FROM notifications");
        deferBlock.Should().NotContain("sent_at = NOW()");
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);
        var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex);
        return text[startIndex..endIndex];
    }
}
