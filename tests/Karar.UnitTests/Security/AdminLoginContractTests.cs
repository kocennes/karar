using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

/// Source contract tests for the admin login endpoint: the email OTP second factor is
/// fail-closed and there is no static admin token / OTP skip flag anymore.
public sealed class AdminLoginContractTests
{
    [Fact]
    public void AdminLogin_HasNoSkipEmailOtpFlag()
    {
        ReadProgram().Should().NotContain("SkipEmailOtp");
    }

    [Fact]
    public void AdminLogin_OtpDeliveryFailure_DoesNotIssueToken()
    {
        var loginBlock = SliceAdminLoginBlock();
        var catchStart = loginBlock.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        catchStart.Should().BeGreaterThanOrEqualTo(0, "OTP send failure must be handled explicitly");

        var catchEnd = loginBlock.IndexOf("ValidateCachedOtpAsync", catchStart, StringComparison.Ordinal);
        catchEnd.Should().BeGreaterThan(catchStart);
        var catchBlock = loginBlock[catchStart..catchEnd];

        catchBlock.Should().NotContain("IssueToken", "a failed OTP email must never fall back to issuing a token");
        catchBlock.Should().NotContain("bruteForce.ClearAsync");
        catchBlock.Should().Contain("ServiceUnavailable(");
        catchBlock.Should().Contain("\"OTP_DELIVERY_FAILED\"");
        catchBlock.Should().Contain("logger.LogError(ex,");
    }

    [Fact]
    public void AdminLogin_EmailNotConfigured_FailsClosedOutsideDevelopmentAndTesting()
    {
        var loginBlock = SliceAdminLoginBlock();
        var branchStart = loginBlock.IndexOf("if (!emailService.IsConfigured)", StringComparison.Ordinal);
        branchStart.Should().BeGreaterThanOrEqualTo(0);

        // Staging (and any other non-dev environment) must refuse too, so the skip is an
        // allow-list of Development/Testing rather than a Production-only refusal.
        const string refusalGuard = "if (!environment.IsDevelopment() && !environment.IsEnvironment(\"Testing\"))";
        var refusalCheck = loginBlock.IndexOf(refusalGuard, branchStart, StringComparison.Ordinal);
        var issueToken = loginBlock.IndexOf("adminAuth.IssueToken()", branchStart, StringComparison.Ordinal);

        refusalCheck.Should().BeGreaterThan(branchStart);
        issueToken.Should().BeGreaterThan(refusalCheck,
            "the refusal must be evaluated before the dev-only OTP skip issues a token");
        loginBlock.Should().NotContain("environment.IsProduction()",
            "a Production-only check would let Staging skip the OTP step");

        var refusalBranch = loginBlock[refusalCheck..issueToken];
        refusalBranch.Should().Contain("logger.LogCritical(");
        refusalBranch.Should().Contain("ServiceUnavailable(");
        refusalBranch.Should().Contain("\"OTP_DELIVERY_FAILED\"");
    }

    [Fact]
    public void AdminLogin_ExpiresAtComesFromIssuedToken()
    {
        var loginBlock = SliceAdminLoginBlock();

        loginBlock.Should().NotContain("DateTimeOffset.UtcNow.AddHours(",
            "expiresAt must come from AdminAuthService.IssueToken(), not a separate hard-coded lifetime");
        loginBlock.Should().Contain("Results.Ok(adminAuth.IssueToken())");
    }

    [Fact]
    public void AdminLogin_KeepsIpAllowlistBruteForceAndAuditLogging()
    {
        var loginBlock = SliceAdminLoginBlock();

        loginBlock.Should().Contain("adminAuth.IsIpAllowed(httpContext)");
        loginBlock.Should().Contain("bruteForce.IsLockedOutAsync(bfIdentity)");
        loginBlock.Should().Contain("bruteForce.RecordFailedAttemptAsync(bfIdentity)");
        loginBlock.Should().Contain("\"login_failed\"");
        loginBlock.Should().Contain(".RequireRateLimiting(\"auth-strict\")");
    }

    [Fact]
    public void ServiceUnavailableHelper_UsesErrorEnvelopeWith503()
    {
        var programText = ReadProgram();

        programText.Should().Contain("static IResult ServiceUnavailable(string code, string message) =>");
        programText.Should().Contain(
            "Results.Json(new ErrorEnvelope(new ErrorBody(code, message)), statusCode: StatusCodes.Status503ServiceUnavailable)");
    }

    [Fact]
    public void AdminAuthService_HasNoStaticTokenMechanism()
    {
        var serviceText = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "AdminAuthService.cs");

        serviceText.Should().NotContain("Admin:Token");
        serviceText.Should().NotContain("_plainToken");
    }

    [Fact]
    public void JwtService_DisablesInboundClaimMapping()
    {
        var jwtText = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "JwtService.cs");

        jwtText.Should().Contain("new JwtSecurityTokenHandler { MapInboundClaims = false }",
            "admin/user claim lookups use raw JWT claim names (sub, email, unique_name)");
    }

    [Theory]
    [InlineData("backend/docker-compose.yml")]
    [InlineData("backend/cloudrun-service.yaml")]
    [InlineData("backend/Karar.Api/appsettings.json")]
    [InlineData("tests/Karar.IntegrationTests/CustomWebApplicationFactory.cs")]
    public void DeploymentAndTestConfig_DoNotConfigureStaticAdminToken(string relativePath)
    {
        var text = TestRepoPaths.ReadText(relativePath.Split('/'));

        text.Should().NotContain("Admin__Token");
        text.Should().NotContain("Admin:Token");
        text.Should().NotContain("SkipEmailOtp");
    }

    private static string ReadProgram() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string SliceAdminLoginBlock()
    {
        var text = ReadProgram();
        const string startMarker = "app.MapPost(\"/api/v1/admin/auth/login\"";
        const string endMarker = "}).RequireRateLimiting(\"auth-strict\");";

        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "admin login endpoint should exist");
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "admin login endpoint should end with auth-strict rate limiting");

        return text[start..(end + endMarker.Length)];
    }
}
