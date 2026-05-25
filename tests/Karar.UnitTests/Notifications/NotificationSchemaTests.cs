using System.Text.RegularExpressions;
using FluentAssertions;

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
        var migrationPath = FindRepoFile("backend/migrations/V38__notification_type_check.sql");
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
}
