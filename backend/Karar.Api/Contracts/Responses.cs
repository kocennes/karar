using Karar.Api.Models;

namespace Karar.Api.Contracts;

public sealed record ErrorEnvelope(ErrorBody Error);

public sealed record ErrorBody(string Code, string Message, int? RetryAfterSeconds = null);

public sealed record MessageResponse(string Message);

public sealed record RegisterResponse(string Message, string Email);

public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    PagedPagination Pagination
);

public sealed record FeedResponse(
    IReadOnlyList<PostDto> Posts,
    Pagination Pagination,
    string? RankingLabel = null
);

public sealed record DiscoverResponse(
    IReadOnlyList<PostDto> Rising,
    IReadOnlyList<PostDto> Controversial,
    IReadOnlyList<PostDto> Fresh,
    IReadOnlyList<PostDto>? CityTrending = null,
    string? City = null
);

public sealed record CommentsResponse(
    IReadOnlyList<CommentDto> Comments,
    Pagination Pagination,
    Guid? RisingCommentId = null
);

public sealed record CategoriesResponse(IReadOnlyList<CategoryDto> Categories);

public sealed record Pagination(int Page, int Limit, int Total, bool HasNext);

public sealed record PagedPagination(int Page, int Limit, int Total, bool HasMore);

public sealed record CreatePostResponse(
    Guid Id,
    string Title,
    string Status,
    DateTimeOffset CreatedAt,
    string Slug = ""
);

public sealed record VoteResponse(
    int VoteCountHakli,
    int VoteCountHaksiz,
    string? MyVote
);

public sealed record CommentMutationResponse(
    Guid Id,
    string Content,
    string Status,
    DateTimeOffset CreatedAt
);

public sealed record UpvoteResponse(int UpvoteCount, bool MyUpvote);

public sealed record ReportResponse(Guid ReportId, string Message);

public sealed record NotificationsResponse(
    IReadOnlyList<NotificationDto> Notifications,
    Pagination Pagination,
    int UnreadCount = 0
);

public sealed record NotificationDto(
    Guid Id,
    string Type,
    string Title,
    string Body,
    Guid? PostId,
    bool IsRead,
    DateTimeOffset CreatedAt
);

public sealed record ModerationQueueItem(
    Guid Id,
    string Type,
    string Title,
    string Content,
    string Status,
    string? ModerationReason,
    int ReportCount,
    DateTimeOffset CreatedAt,
    Guid DeviceId,
    double? PerspectiveToxicity = null,
    string? ImageUrl = null
);

public sealed record AdminReportDto(
    Guid Id,
    string TargetType,
    Guid TargetId,
    string Reason,
    string? Description,
    string Status,
    DateTimeOffset CreatedAt,
    string? TargetContent = null,
    string? TargetTitle = null
);

public sealed record AdminPostDto(
    Guid Id,
    string Title,
    string Content,
    string Status,
    int CategoryId,
    int VoteCountHakli,
    int VoteCountHaksiz,
    int CommentCount,
    string? ImageUrl,
    DateTimeOffset CreatedAt,
    Guid DeviceId,
    Guid? UserId = null,
    string? AuthorName = null,
    bool IsAnonymous = false
);

public sealed record ModerationTransparencyResponse(
    int PeriodDays = 30,
    long PostsCreated = 0,
    long PostsRemoved = 0,
    double PostRemovalRatePercent = 0,
    long CommentsCreated = 0,
    long CommentsRemoved = 0,
    double CommentRemovalRatePercent = 0,
    long ReportsReceived = 0,
    long ReportsActioned = 0,
    double ReportActionRatePercent = 0,
    ModerationReasons? RemovalReasons = null,
    DateTimeOffset LastUpdated = default
);

public sealed record ModerationReasons(
    long Harassment = 0,
    long HateSpeech = 0,
    long Spam = 0,
    long Misinformation = 0,
    long Other = 0
);

public sealed record AdminDeviceDto(
    Guid Id,
    string Fingerprint,
    string Platform,
    bool IsBanned,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    int PostCount = 0,
    int CommentCount = 0,
    int ReportCount = 0,
    string? CurrentBanReason = null,
    DateTimeOffset? BanExpiresAt = null
);

public sealed record AdminCommentDto(
    Guid Id,
    Guid PostId,
    string Content,
    string Status,
    int UpvoteCount,
    int DownvoteCount,
    DateTimeOffset CreatedAt,
    Guid DeviceId,
    Guid? UserId
);

public sealed record AdminUserDto(
    Guid Id,
    Guid DeviceId,
    string Username,
    string Email,
    int Karma,
    string AuthProvider,
    bool EmailVerified,
    bool IsBanned,
    DateTimeOffset? BanExpiresAt,
    string? BanReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeletedAt,
    int PostCount,
    int CommentCount
);

public sealed record AdminOverviewResponse(
    AdminOverviewStats Stats
);

public sealed record AdminOverviewStats(
    int DauToday,
    int DauYesterday,
    int PostsToday,
    int PendingReports,
    int UnderReviewPosts,
    int TotalPosts,
    int TotalComments,
    int TotalVotes
);

public sealed record AdminLoginResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt
);

public sealed record AuthTokensResponse(
    Guid UserId,
    string Username,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    bool IsNewUser = false,
    UserProfile? User = null
);

public sealed record UserProfile(
    Guid Id,
    string Username,
    string Email,
    int Karma,
    string AuthProvider,
    bool EmailVerified,
    DateTimeOffset? JoinedAt = null,
    int PostCount = 0,
    int CommentCount = 0,
    bool Is2faEnabled = false,
    string? Bio = null,
    DateTimeOffset? UsernameChangedAt = null
);

public sealed record TwoFactorSetupResponse(
    string Secret,
    string OtpAuthUrl
);

public sealed record UserSessionDto(
    Guid Id,
    string Platform,
    DateTimeOffset LastSeenAt,
    bool IsCurrent
);

public sealed record AdminActionDto(
    Guid Id,
    string AdminEmail,
    string Action,
    string TargetType,
    Guid? TargetId,
    string? Note,
    DateTimeOffset CreatedAt
);

public sealed record MyCommentDto(
    Guid Id,
    string Content,
    int UpvoteCount,
    int DownvoteCount,
    bool IsEdited,
    bool IsPinned,
    DateTimeOffset CreatedAt,
    Guid PostId,
    string PostTitle,
    string Status = "active"
);

public sealed record KarmaHistoryDto(
    Guid Id,
    string SourceType,
    Guid SourceId,
    int Milestone,
    int KarmaDelta,
    DateTimeOffset CreatedAt
);

public sealed record WeeklyStatsDto(
    string WeekLabel,
    int KarmaEarned,
    int VotesGiven,
    int HakliGiven,
    int HaksizGiven,
    int PostsCreated,
    int CommentsPosted,
    int Streak
);
