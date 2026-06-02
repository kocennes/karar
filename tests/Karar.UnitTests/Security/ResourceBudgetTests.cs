using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class ResourceBudgetTests
{
    // ── Search ─────────────────────────────────────────────────────────────

    [Fact]
    public void SearchEndpoint_EnforcesPageSizeAndCommandTimeout()
    {
        var programText = ReadProgram();
        var searchBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/search\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"");

        searchBlock.Should().Contain("limit = Math.Clamp(limit, 1, 50);");
        searchBlock.Should().Contain("command.CommandTimeout = 5;");
        searchBlock.Should().Contain("EnforceMinimumResponseTimeAsync(responseTimer");
        programText.Should().Contain("TimeSpan.FromMilliseconds(200)");
    }

    // ── Data Export ────────────────────────────────────────────────────────

    [Fact]
    public void DataExportEndpoint_EnforcesDailyBudgetAndTimeout()
    {
        var programText = ReadProgram();
        var exportBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/users/me/data-export\"",
            "app.MapPost(\"/api/v1/users/me/blocked\"");

        exportBlock.Should().Contain("redis.IsAllowedAsync(");
        exportBlock.Should().Contain("\"data-export\"");
        exportBlock.Should().Contain("limit: 1");
        exportBlock.Should().Contain("TimeSpan.FromDays(1)");
        exportBlock.Should().Contain("DATA_EXPORT_LIMIT");
        exportBlock.Should().Contain("command.CommandTimeout = 10;");
    }

    // ── Report ─────────────────────────────────────────────────────────────

    [Fact]
    public void ReportEndpoint_EnforcesRateLimitsAndRequiresDeviceToken()
    {
        var programText = ReadProgram();
        var reportBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/reports\"",
            "app.MapPost(\"/api/v1/feedback\"");

        // Redis-backed device rate limit via ReportAbuseProtectionService (multi-instance safe)
        reportBlock.Should().Contain("abuseProtection.CheckDeviceRateLimitAsync(");
        // 429 response includes Retry-After header
        reportBlock.Should().Contain("Retry-After");
        reportBlock.Should().Contain("RATE_LIMIT_REPORTS");
        // Unauthenticated (no device token) is rejected
        reportBlock.Should().Contain("return Unauthorized()");
    }

    [Fact]
    public void ReportEndpoint_BlocksDuplicateReportsViaOnConflict()
    {
        var programText = ReadProgram();
        var reportBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/reports\"",
            "app.MapPost(\"/api/v1/feedback\"");

        // DB-level uniqueness: one report per device per target
        reportBlock.Should().Contain("ON CONFLICT (reporter_device_id, target_type, target_id) DO NOTHING");
        reportBlock.Should().Contain("REPORT_EXISTS");
    }

    [Fact]
    public void CreateReportRequest_DescriptionFieldHasLengthCap()
    {
        var requestsText = ReadRequests();
        // Description must be capped to prevent oversized payloads
        requestsText.Should().Contain("[StringLength(300)] string? Description");
    }

    // ── Create Post ────────────────────────────────────────────────────────

    [Fact]
    public void CreatePostEndpoint_EnforcesImageSizeModerationAndHourlyLimit()
    {
        var programText = ReadProgram();
        var postBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/posts\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}\"");

        // ASP.NET rate-limiter policy (IP-level)
        postBlock.Should().Contain("RequireRateLimiting(\"post-create\")");
        // Per-device / per-user hourly post cap (DB-level second layer)
        postBlock.Should().Contain("DAILY_POST_LIMIT");
        // 5 MB image size hard limit
        postBlock.Should().Contain("5 * 1024 * 1024");
        postBlock.Should().Contain("IMAGE_TOO_LARGE");
        // Content moderation pipeline must run before insert
        postBlock.Should().Contain("moderationService.Analyze(");
        postBlock.Should().Contain("perspectiveService.AnalyzeAsync(");
        // Category throttle check now slows distribution instead of blocking creation.
        postBlock.Should().Contain("categoryThrottle.GetStatusAsync(");
    }

    [Fact]
    public void CreatePostRequest_TitleAndContentHaveLengthConstraints()
    {
        var requestsText = ReadRequests();
        // Title: 10–120 chars
        requestsText.Should().Contain("StringLength(120, MinimumLength = 10)");
        // Content: 50–1500 chars
        requestsText.Should().Contain("StringLength(1500, MinimumLength = 50)");
    }

    // ── Comment ────────────────────────────────────────────────────────────

    [Fact]
    public void CreateCommentEndpoint_EnforcesPerDeviceHourlyRateLimit()
    {
        var programText = ReadProgram();
        var commentBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapDelete(\"/api/v1/comments/{id:guid}\"");

        // Redis sliding-window rate limit (30 comments / device / hour)
        commentBlock.Should().Contain("redis.IsAllowedAsync(");
        commentBlock.Should().Contain("\"comment-create\"");
        commentBlock.Should().Contain("limit: 30");
        commentBlock.Should().Contain("TimeSpan.FromHours(1)");
        // 429 response with readable error code
        commentBlock.Should().Contain("RATE_LIMIT_COMMENTS");
        commentBlock.Should().Contain("TooManyRequests(");
    }

    [Fact]
    public void CreateCommentRequest_ContentHasLengthConstraints()
    {
        var requestsText = ReadRequests();
        // Content: 5–500 chars
        requestsText.Should().Contain("StringLength(500, MinimumLength = 5)");
    }

    [Fact]
    public void CreateCommentEndpoint_RunsModerationBeforeInsert()
    {
        var programText = ReadProgram();
        var commentBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/comments\"",
            "app.MapDelete(\"/api/v1/comments/{id:guid}\"");

        // Content moderation pipeline must run before DB insert
        commentBlock.Should().Contain("moderationService.Analyze(request.Content)");
        commentBlock.Should().Contain("CONTENT_REJECTED");
    }

    // ── Auth — Login ───────────────────────────────────────────────────────

    [Fact]
    public void AuthLoginEndpoint_HasBruteForceProtectionProgressiveDelayAndRateLimit()
    {
        var programText = ReadProgram();
        var loginBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/login\"",
            "app.MapPost(\"/api/v1/auth/refresh\"");

        loginBlock.Should().Contain("RequireRateLimiting(\"auth-strict\")");
        // Lockout check before any DB lookup
        loginBlock.Should().Contain("bruteForce.IsLockedOutAsync(");
        loginBlock.Should().Contain("ACCOUNT_LOCKED");
        // Progressive server-side delay on repeated failures
        loginBlock.Should().Contain("BruteForceService.ComputeDelayMs(");
        // Failed attempt recording to increment counter
        loginBlock.Should().Contain("bruteForce.RecordFailedAttemptAsync(");
    }

    // ── Auth — Register ────────────────────────────────────────────────────

    [Fact]
    public void AuthRegisterEndpoint_RequiresDeviceTokenAndRateLimit()
    {
        var programText = ReadProgram();
        var registerBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/register\"",
            "app.MapPost(\"/api/v1/auth/verify-email\"");

        registerBlock.Should().Contain("RequireRateLimiting(\"auth-strict\")");
        registerBlock.Should().Contain("deviceId is null");
        registerBlock.Should().Contain("return Unauthorized()");
    }

    // ── Auth — Forgot / Reset Password ─────────────────────────────────────

    [Fact]
    public void AuthForgotPasswordEndpoint_UsesGenericResponseAndEnforcesResendCooldown()
    {
        var programText = ReadProgram();
        var forgotBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/forgot-password\"",
            "app.MapPost(\"/api/v1/auth/reset-password\"");

        forgotBlock.Should().Contain("RequireRateLimiting(\"auth-strict\")");
        // Generic OK response regardless of whether e-mail is registered (prevents enumeration)
        forgotBlock.Should().Contain("Eğer bu e-posta kayıtlıysa");
        // OTP resend cooldown must prevent flooding
        forgotBlock.Should().Contain("OTP_TOO_SOON");
        forgotBlock.Should().Contain("TimeSpan.FromMinutes(3)");
    }

    [Fact]
    public void AuthResetPasswordEndpoint_LimitsOtpAttemptsAndChecksExpiry()
    {
        var programText = ReadProgram();
        var resetBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/reset-password\"",
            "app.MapGet(\"/api/v1/admin/analytics/cache\"");

        resetBlock.Should().Contain("RequireRateLimiting(\"auth-strict\")");
        // OTP attempt cap: 3 strikes then force new code request
        resetBlock.Should().Contain("attempts >= 3");
        resetBlock.Should().Contain("OTP_MAX_ATTEMPTS");
        // Expired OTP must be rejected
        resetBlock.Should().Contain("OTP_EXPIRED");
    }

    // ── Vote — Brigade Guard ───────────────────────────────────────────────

    [Fact]
    public void VoteEndpoint_InvokesInlineBrigadeGuardAfterUpsert()
    {
        var programText = ReadProgram();
        var voteBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        // Brigade guard must be injected
        voteBlock.Should().Contain("VoteBrigadeGuard brigadeGuard");
        // CheckAndSuppressAsync must be called inside the vote mutation branch
        voteBlock.Should().Contain("brigadeGuard.CheckAndSuppressAsync(");
        // Result must be checked before MarkPostDirtyAsync
        voteBlock.Should().Contain("brigadeResult.Detected");
    }

    [Fact]
    public void VoteEndpoint_BrigadeDetectionSupportsHiddenContentGuardrail()
    {
        // Suppressed votes must NOT manipulate the public verdict; the trend score
        // updater already filters is_quarantined=FALSE.  Verify the endpoint never
        // calls MarkPostDirtyAsync when brigade is detected.
        var programText = ReadProgram();
        var voteBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        // Both trust-quarantine AND brigade-suppression must be checked
        voteBlock.Should().Contain("!trustDecision.ShouldQuarantineVote && !brigadeResult.Detected",
            "MarkPostDirtyAsync must be guarded by both trust and brigade result");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string ReadProgram() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string ReadRequests() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Contracts", "Requests.cs");

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }
}
