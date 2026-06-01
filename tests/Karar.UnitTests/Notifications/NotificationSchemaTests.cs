using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.Api.Models;

namespace Karar.UnitTests.Notifications;

public sealed class NotificationSchemaTests
{
    private static readonly string[] ExpectedNotificationTypes =
    [
        "comment_on_post",
        "reply_on_comment",
        "verdict_milestone",
        "verdict_reminder",
        "moderation_result",
        "mention",
        "follow",
        "follow_new_post",
        "system_announcement",
        "trend_alert",
        "viral_post_owner",
        "weekly_digest"
    ];

    private static readonly string[] ExpectedDeliveryFields =
    [
        "payload",
        "priority",
        "dedupe_key",
        "read_at",
        "dismissed_at",
        "attempt_count",
        "last_error",
        "failed_at",
        "next_attempt_at",
        "provider_message_id"
    ];

    [Fact]
    public void LatestNotificationTypeConstraint_AllowsAllKnownNotificationTypes()
    {
        var migrationPath = FindRepoFile("backend/migrations/V44__notification_follow_new_post_type.sql");
        var migrationSql = File.ReadAllText(migrationPath);
        var allowedTypes = Regex.Matches(migrationSql, "'([a-z_]+)'")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missingTypes = ExpectedNotificationTypes
            .Where(type => !allowedTypes.Contains(type))
            .ToArray();

        missingTypes.Should().BeEmpty();
    }

    [Fact]
    public void DeliveryFieldsMigration_AddsProductionNotificationFieldsAndIndexes()
    {
        var migrationPath = FindRepoFile("backend/migrations/V39__notification_delivery_fields.sql");
        var migrationSql = File.ReadAllText(migrationPath);
        var normalizedSql = Regex.Replace(migrationSql, @"\s+", " ").ToLowerInvariant();

        foreach (var field in ExpectedDeliveryFields)
        {
            normalizedSql.Should().Contain($"add column if not exists {field}");
        }

        normalizedSql.Should().Contain("notifications_priority_check");
        normalizedSql.Should().Contain("priority in ('low', 'normal', 'high', 'critical')");
        normalizedSql.Should().Contain("notifications_attempt_count_check");
        normalizedSql.Should().Contain("attempt_count >= 0");
        normalizedSql.Should().Contain("idx_notifications_unsent_next_attempt");
        normalizedSql.Should().Contain("sent_at is null and failed_at is null");
        normalizedSql.Should().Contain("idx_notifications_device_visible");
        normalizedSql.Should().Contain("dismissed_at is null");
        normalizedSql.Should().Contain("ux_notifications_dedupe_key");
        normalizedSql.Should().Contain("where dedupe_key is not null");
    }

    private static readonly string[] ExpectedEventTypes =
    [
        "intent", "eligible", "suppressed",
        "send_attempt", "sent", "failed", "retrying",
        "opened", "dismissed", "read"
    ];

    [Fact]
    public void NotificationEventsMigration_HasCorrectEventTypesAndIndexes()
    {
        var migrationPath = FindRepoFile("backend/migrations/V40__notification_events.sql");
        var migrationSql = File.ReadAllText(migrationPath);
        var normalizedSql = Regex.Replace(migrationSql, @"\s+", " ").ToLowerInvariant();

        normalizedSql.Should().Contain("create table notification_events");
        normalizedSql.Should().Contain("notification_events_type_check");

        foreach (var eventType in ExpectedEventTypes)
        {
            normalizedSql.Should().Contain($"'{eventType}'",
                because: $"event type '{eventType}' must be in the CHECK constraint");
        }

        normalizedSql.Should().Contain("idx_notification_events_notification");
        normalizedSql.Should().Contain("idx_notification_events_device");
        normalizedSql.Should().Contain("idx_notification_events_type_time");
    }

    [Fact]
    public void NotificationTypeConstants_AllPresentInLatestConstraintMigration()
    {
        var constants = typeof(NotificationTypes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        var migrationPath = FindRepoFile("backend/migrations/V44__notification_follow_new_post_type.sql");
        var migrationSql = File.ReadAllText(migrationPath);
        var allowedTypes = Regex.Matches(migrationSql, "'([a-z_]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missingTypes = constants
            .Where(type => !allowedTypes.Contains(type))
            .ToArray();

        missingTypes.Should().BeEmpty(
            because: "every NotificationTypes constant must appear in the V44 migration constraint — " +
                     "add missing types to V44 or create a new migration that extends the CHECK constraint");
    }

    [Fact]
    public void FcmTokenDeleteEndpoint_RemovesOnlyCurrentDeviceTokens()
    {
        var programPath = FindRepoFile("backend/Karar.Api/Program.cs");
        var program = File.ReadAllText(programPath);
        var endpointBlock = Slice(
            program,
            "app.MapDelete(\"/api/v1/devices/fcm-token\"",
            "// Public moderation transparency report");

        endpointBlock.Should().Contain("requestDevice.TryGetDeviceIdAsync(httpRequest)",
            "FCM token deletion must be scoped to the caller's device token");
        endpointBlock.Should().Contain("DELETE FROM fcm_tokens WHERE device_id = @deviceId",
            "logout token cleanup must not delete tokens owned by other devices");
    }

    [Fact]
    public void DispatcherFailureUpdates_DoNotDeleteInAppNotificationRows()
    {
        var dispatcherPath = FindRepoFile("backend/Karar.Api/Services/NotificationDispatcher.cs");
        var dispatcher = File.ReadAllText(dispatcherPath);
        var permanentFailureBlock = Slice(
            dispatcher,
            "if (decision.Action == DeliveryAction.MarkFailed)",
            "// ScheduleRetry");
        var retryBlock = Slice(
            dispatcher,
            "// ScheduleRetry",
            "private async Task MarkDedupSuppressedAsync");

        permanentFailureBlock.Should().Contain("UPDATE notifications");
        permanentFailureBlock.Should().Contain("failed_at = NOW()");
        permanentFailureBlock.Should().Contain("status = 'permanently_failed'");
        permanentFailureBlock.Should().Contain("InsertDeadLetterAsync");
        permanentFailureBlock.Should().NotContain("DELETE FROM notifications");

        retryBlock.Should().Contain("UPDATE notifications");
        retryBlock.Should().Contain("attempt_count = attempt_count + 1");
        retryBlock.Should().Contain("next_attempt_at = NOW()");
        retryBlock.Should().NotContain("sent_at = NOW()");
        retryBlock.Should().NotContain("DELETE FROM notifications");
    }

    [Fact]
    public void DispatcherDeadLetters_DistinguishPermanentFailuresFromMaxAttempts()
    {
        var dispatcherPath = FindRepoFile("backend/Karar.Api/Services/NotificationDispatcher.cs");
        var dispatcher = File.ReadAllText(dispatcherPath);
        var failureBlock = Slice(
            dispatcher,
            "if (decision.Action == DeliveryAction.MarkFailed)",
            "// ScheduleRetry");

        failureBlock.Should().Contain("result.Status == PushSendStatus.PermanentFailure");
        failureBlock.Should().Contain("\"permanent_failure\"");
        failureBlock.Should().Contain("\"max_attempts_exceeded\"");
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
