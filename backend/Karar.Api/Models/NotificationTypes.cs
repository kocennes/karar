namespace Karar.Api.Models;

/// Single source of truth for all valid notification type strings.
/// Every value here must appear in the latest notifications_type_check migration constraint.
public static class NotificationTypes
{
    public const string CommentOnPost = "comment_on_post";
    public const string ReplyOnComment = "reply_on_comment";
    public const string VerdictMilestone = "verdict_milestone";
    public const string VerdictReminder = "verdict_reminder";
    public const string ModerationResult = "moderation_result";
    public const string Mention = "mention";
    public const string Follow = "follow";
    public const string FollowNewPost = "follow_new_post";
    public const string SystemAnnouncement = "system_announcement";
    public const string TrendAlert = "trend_alert";
    public const string ViralPostOwner = "viral_post_owner";
    public const string WeeklyDigest = "weekly_digest";
    public const string CrisisSupport = "crisis_support";
}
