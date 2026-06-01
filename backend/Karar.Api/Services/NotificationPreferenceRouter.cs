using System.Text.Json;
using Karar.Api.Contracts;
using Karar.Api.Models;
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

    /// <summary>
    /// Returns the push decision. When <see cref="PushDecision.Allowed"/> is false,
    /// <see cref="PushDecision.SuggestedRetryDelay"/> hints at how long the dispatcher should wait
    /// before retrying (e.g. jump to quiet-hours end rather than the default 15 min backoff).
    /// </summary>
    public async Task<PushDecision> CanPushAsync(
        Guid deviceId,
        string notificationType,
        NotificationPriority priority,
        NpgsqlConnection connection,
        CancellationToken ct = default)
    {
        if (priority == NotificationPriority.Critical)
        {
            return PushDecision.Allow();
        }

        var prefs = await LoadPreferencesAsync(deviceId, connection, ct);
        if (prefs is null)
        {
            return PushDecision.Allow();
        }

        if (prefs.PushEnabled == false)
        {
            return PushDecision.Block();
        }

        if (prefs.MutedUntil.HasValue && prefs.MutedUntil.Value > DateTimeOffset.UtcNow)
        {
            var muteRemaining = prefs.MutedUntil.Value - DateTimeOffset.UtcNow;
            return PushDecision.Block(muteRemaining);
        }

        // Quiet hours check
        if (!string.IsNullOrEmpty(prefs.QuietHoursStart) && !string.IsNullOrEmpty(prefs.QuietHoursEnd))
        {
            var delay = GetQuietHoursDelay(prefs.QuietHoursStart, prefs.QuietHoursEnd);
            if (delay.HasValue)
            {
                return PushDecision.Block(delay);
            }
        }

        return IsCategoryEnabled(notificationType, prefs)
            ? PushDecision.Allow()
            : PushDecision.Block();
    }

    /// <summary>
    /// Returns a delay until quiet hours end when current UTC time is inside the window;
    /// returns null when outside the quiet window.
    /// </summary>
    public static TimeSpan? GetQuietHoursDelay(string startStr, string endStr)
    {
        if (!TryParseHhmm(startStr, out int startH, out int startM) ||
            !TryParseHhmm(endStr, out int endH, out int endM))
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var startMinutes = startH * 60 + startM;
        var endMinutes = endH * 60 + endM;
        var nowMinutes = now.Hour * 60 + now.Minute;

        bool inWindow;
        if (startMinutes < endMinutes)
        {
            // Same-day window e.g. 01:00–07:00
            inWindow = nowMinutes >= startMinutes && nowMinutes < endMinutes;
        }
        else
        {
            // Overnight window e.g. 22:00–08:00
            inWindow = nowMinutes >= startMinutes || nowMinutes < endMinutes;
        }

        if (!inWindow) return null;

        // Calculate time until end of quiet window
        var endToday = now.Date.AddMinutes(endMinutes);
        var endCandidate = endToday > now ? endToday : endToday.AddDays(1);
        return endCandidate - now;
    }

    private static bool TryParseHhmm(string value, out int hour, out int minute)
    {
        hour = minute = 0;
        var parts = value.Split(':');
        return parts.Length == 2
            && int.TryParse(parts[0], out hour)
            && int.TryParse(parts[1], out minute)
            && hour is >= 0 and < 24
            && minute is >= 0 and < 60;
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

    public static bool IsCategoryEnabled(string type, NotificationPreferencesRequest prefs) => type switch
    {
        NotificationTypes.CommentOnPost => prefs.NotifyOnComment ?? true,
        NotificationTypes.ReplyOnComment => prefs.NotifyOnReply ?? true,
        NotificationTypes.VerdictMilestone or NotificationTypes.VerdictReminder or NotificationTypes.ViralPostOwner => prefs.NotifyOnVerdict ?? true,
        NotificationTypes.ModerationResult => prefs.NotifyOnPostStatus ?? true,
        NotificationTypes.Mention or NotificationTypes.Follow => prefs.NotifyOnMention ?? true,
        NotificationTypes.TrendAlert => prefs.NotifyOnTrend ?? false,
        NotificationTypes.WeeklyDigest => prefs.NotifyOnDigest ?? false,
        NotificationTypes.FollowNewPost => prefs.NotifyOnTrend ?? false,
        NotificationTypes.SystemAnnouncement => true,
        _ => true,
    };
}

public readonly record struct PushDecision(bool Allowed, TimeSpan? SuggestedRetryDelay)
{
    public static PushDecision Allow() => new(true, null);
    public static PushDecision Block(TimeSpan? retryDelay = null) => new(false, retryDelay);
}
