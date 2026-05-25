using System.Text.Json;
using Karar.Api.Contracts;
using Npgsql;

namespace Karar.Api.Services;

/// <summary>
/// Checks whether a notification should be delivered as push based on the user's
/// stored notification_preferences. Critical-priority notifications bypass all checks.
/// Guest devices (no associated user) are always allowed through.
/// </summary>
public sealed class NotificationPreferenceRouter(ILogger<NotificationPreferenceRouter> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> CanPushAsync(
        Guid deviceId,
        string notificationType,
        NotificationPriority priority,
        NpgsqlConnection connection,
        CancellationToken ct = default)
    {
        if (priority == NotificationPriority.Critical)
        {
            return true;
        }

        var prefs = await LoadPreferencesAsync(deviceId, connection, ct);
        if (prefs is null)
        {
            return true;
        }

        if (prefs.PushEnabled == false)
        {
            return false;
        }

        if (prefs.MutedUntil.HasValue && prefs.MutedUntil.Value > DateTimeOffset.UtcNow)
        {
            return false;
        }

        return IsCategoryEnabled(notificationType, prefs);
    }

    private async Task<NotificationPreferencesRequest?> LoadPreferencesAsync(
        Guid deviceId,
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        try
        {
            await using var cmd = new NpgsqlCommand(
                """
                SELECT notification_preferences
                FROM users
                WHERE device_id = @deviceId AND deleted_at IS NULL
                LIMIT 1
                """,
                connection);
            cmd.Parameters.AddWithValue("deviceId", deviceId);

            var json = await cmd.ExecuteScalarAsync(ct) as string;
            if (string.IsNullOrEmpty(json) || json == "{}")
            {
                return null;
            }

            return JsonSerializer.Deserialize<NotificationPreferencesRequest>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load notification preferences for device {DeviceId}", deviceId);
            return null;
        }
    }

    private static bool IsCategoryEnabled(string type, NotificationPreferencesRequest prefs) => type switch
    {
        "comment_on_post" => prefs.NotifyOnComment ?? true,
        "reply_on_comment" => prefs.NotifyOnReply ?? true,
        "verdict_milestone" or "viral_post_owner" => prefs.NotifyOnVerdict ?? true,
        "moderation_result" => prefs.NotifyOnPostStatus ?? true,
        "mention" => prefs.NotifyOnMention ?? true,
        "trend_alert" => prefs.NotifyOnTrend ?? false,
        "weekly_digest" => prefs.NotifyOnDigest ?? false,
        "follow_new_post" => prefs.NotifyOnTrend ?? false,
        "system_announcement" => true,
        _ => true,
    };
}
