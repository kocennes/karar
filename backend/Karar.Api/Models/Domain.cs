namespace Karar.Api.Models;

using System.Text.Json.Serialization;

public sealed record DeviceSession(
    Guid DeviceId,
    string DeviceToken,
    DateTimeOffset ExpiresAt
);

public sealed record CategoryDto(int Id, string Name, string Emoji, string Slug = "");

public sealed record PostDto(
    Guid Id,
    string Title,
    string Content,
    string? ImageUrl,
    CategoryDto Category,
    int VoteCountHakli,
    int VoteCountHaksiz,
    int CommentCount,
    string? MyVote,
    double TrendScore,
    DateTimeOffset CreatedAt,
    bool IsOwner,
    string? Status = null,
    string? ModerationReason = null,
    bool IsEdited = false,
    bool IsSaved = false,
    string? AuthorName = null,
    Guid? AuthorId = null,
    bool IsUnlisted = false,
    bool IsAnonymous = false,
    bool IsClosed = false,
    [property: JsonPropertyName("ranking_reason")]
    string? RankingReason = null,
    [property: JsonPropertyName("ranking_label")]
    string? RankingLabel = null,
    IReadOnlyList<string>? Tags = null,
    string? AiSummary = null,
    [property: JsonIgnore]
    Guid? RankingAuthorKey = null
);

public sealed record CommentDto(
    Guid Id,
    string Content,
    int UpvoteCount,
    int DownvoteCount,
    bool MyUpvote,
    bool MyDownvote,
    bool IsOwner,
    DateTimeOffset CreatedAt,
    bool IsPinned = false,
    string? AuthorName = null,
    Guid? AuthorId = null,
    bool IsEdited = false,
    bool IsPostOwner = false,
    int UpvotesHakli = 0,
    int UpvotesHaksiz = 0,
    IReadOnlyDictionary<string, int>? Reactions = null,
    string? MyReaction = null,
    Guid? ParentId = null,
    IReadOnlyList<CommentDto>? Replies = null
);
