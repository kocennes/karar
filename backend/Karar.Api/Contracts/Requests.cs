using System.ComponentModel.DataAnnotations;

namespace Karar.Api.Contracts;

public sealed record RegisterDeviceRequest(
    [Required, MinLength(8)] string Fingerprint,
    [Required] string Platform,
    [Required] string AppVersion,
    string? IntegrityToken = null,
    string? Nonce = null
);

public sealed record DeleteAccountRequest(
    [StringLength(72, MinimumLength = 8)] string? Password
);

public sealed record FcmTokenRequest(
    [Required, MinLength(16)] string Token,
    [Required] string Platform
);

public sealed record CreatePostRequest(
    [Required, StringLength(120, MinimumLength = 10)] string Title,
    [Required, StringLength(1500, MinimumLength = 50)] string Content,
    [Range(1, int.MaxValue)] int CategoryId,
    string? ImageUrl = null,
    bool IsUnlisted = false,
    IReadOnlyList<string>? Tags = null
);

public sealed record VoteRequest([Required] string VoteType);

public sealed record PostViewRequest(
    [Range(3, 600)] int? DwellSeconds = null,
    bool WasInteracted = false
);

public sealed record CreateCommentRequest(
    [Required, StringLength(500, MinimumLength = 5)] string Content,
    Guid? ParentId = null
);

public sealed record UpdatePostRequest(
    [Required, StringLength(120, MinimumLength = 10)] string Title,
    [Required, StringLength(1500, MinimumLength = 50)] string Content
);

public sealed record UpdateCommentRequest(
    [Required, StringLength(500, MinimumLength = 5)] string Content
);

public sealed record CommentReactionRequest(
    [Required] string Emoji
);

public sealed record UserIdRequest([Required] Guid UserId);

public sealed record NotificationPreferencesRequest(
    bool? NotifyOnComment,
    bool? NotifyOnReply,
    bool? NotifyOnVerdict,
    bool? NotifyOnPostStatus,
    bool? EmailWeeklySummary,
    string? QuietHoursStart,
    string? QuietHoursEnd
);

public sealed record TwoFactorCodeRequest(
    [Required, StringLength(6, MinimumLength = 6)] string Code
);

public sealed record CreateReportRequest(
    [Required] string TargetType,
    [Required] Guid TargetId,
    [Required] string Reason,
    [StringLength(300)] string? Description
);

public sealed record CreateFeedbackRequest(
    [Required] string Type,
    [Required, StringLength(120, MinimumLength = 5)] string Subject,
    [Required, StringLength(2000, MinimumLength = 10)] string Message,
    [StringLength(120)] string? ContactEmail,
    [StringLength(200)] string? AppVersion,
    [StringLength(80)] string? Platform
);

public sealed record CreateModerationAppealRequest(
    [Required] string TargetType,
    [Required] Guid TargetId,
    [Required, StringLength(1000, MinimumLength = 20)] string Message
);

public sealed record AdminReportActionRequest(
    [Required] string Action,
    [StringLength(500)] string? Note
);

public sealed record AdminBanDeviceRequest(
    [Required] string Type,
    [Required, StringLength(500, MinimumLength = 3)] string Reason,
    [Range(1, 3650)] int? DurationDays
);

public sealed record AdminBanUserRequest(
    [Required, StringLength(500, MinimumLength = 3)] string Reason,
    [Range(1, 3650)] int? DurationDays
);

public sealed record AdminWarnUserRequest(
    [Required, StringLength(500, MinimumLength = 3)] string Message
);

public sealed record AdminStrikeUserRequest(
    [Required, StringLength(500, MinimumLength = 3)] string Reason,
    [Required] string Severity,
    [StringLength(500)] string? Note = null
);

public sealed record AdminThrottleCategoryRequest(
    [Required, StringLength(500, MinimumLength = 3)] string Reason,
    [Range(1, 168)] int DurationHours = 4
);

public sealed record PostFeedbackRequest(
    [Required] string Type
);

public sealed record AdminLoginRequest(
    [Required, EmailAddress] string Email,
    [Required] string Password,
    [Required, StringLength(6, MinimumLength = 6)] string TotpCode
);

public sealed record BulkModerationRequest(
    [Required] string Action,
    [Required, MinLength(1)] List<ModerationBatchItem> Items
);

public sealed record ModerationBatchItem(
    [Required] Guid Id,
    [Required] string Type
);

public sealed record RegisterRequest(
    [Required, RegularExpression(@"^[a-zA-Z0-9_]{3,20}$",
        ErrorMessage = "Kullanıcı adı 3-20 karakter, yalnızca harf/rakam/alt çizgi.")] string Username,
    [Required, EmailAddress] string Email,
    [Required, StringLength(72, MinimumLength = 8)] string Password,
    [Required] DateTime DateOfBirth,
    [Required] string Gender
);

public sealed record VerifyEmailRequest(
    [Required, EmailAddress] string Email,
    [Required, StringLength(6, MinimumLength = 6)] string Otp
);

public sealed record ResendOtpRequest(
    [Required, EmailAddress] string Email
);

public sealed record GoogleSignInRequest(
    [Required] string IdToken
);

public sealed record LoginRequest(
    [Required] string Identifier,
    [Required] string Password,
    [StringLength(6, MinimumLength = 6)] string? TotpCode = null,
    string? BackupCode = null
);

public sealed record RefreshTokenRequest(
    [Required] string RefreshToken
);

public sealed record UpdateUserProfileRequest(
    [RegularExpression(@"^[a-zA-Z0-9_]{3,20}$",
        ErrorMessage = "Kullanıcı adı 3-20 karakter, yalnızca harf/rakam/alt çizgi.")] string Username
);

public sealed record ChangePasswordRequest(
    [Required] string CurrentPassword,
    [Required, StringLength(72, MinimumLength = 8)] string NewPassword
);

public sealed record ForgotPasswordRequest(
    [Required, EmailAddress] string Email
);

public sealed record ResetPasswordRequest(
    [Required, EmailAddress] string Email,
    [Required, StringLength(6, MinimumLength = 6)] string Otp,
    [Required, StringLength(72, MinimumLength = 8)] string NewPassword
);

public sealed record ChangeEmailRequest(
    [Required, EmailAddress] string NewEmail,
    [Required, StringLength(72, MinimumLength = 8)] string Password
);

public sealed record ConfirmChangeEmailRequest(
    [Required, EmailAddress] string NewEmail,
    [Required, StringLength(6, MinimumLength = 6)] string Otp
);

public sealed record RecoverAccountRequest(
    [Required] string Token
);
