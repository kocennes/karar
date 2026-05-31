using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

/// Contract tests for AppAttestationMiddleware and RequireAttestationAttribute.
///
/// The middleware has DB and HTTP context dependencies, so these tests verify
/// design contracts (source shape, registration, attribute API) rather than
/// exercising the live middleware pipeline.
public sealed class AppAttestationMiddlewareTests
{
    [Fact]
    public void RequireAttestationAttribute_ExposesEndpointKey()
    {
        var attributeText = ReadFile(
            "backend", "Karar.Api", "Common", "Attributes", "RequireAttestationAttribute.cs");

        attributeText.Should().Contain("public string EndpointKey");
        attributeText.Should().Contain("AttributeUsage");
        attributeText.Should().Contain("sealed class RequireAttestationAttribute");
    }

    [Fact]
    public void AppAttestationMiddleware_IsRegisteredInPipeline()
    {
        var programText = ReadFile("backend", "Karar.Api", "Program.cs");

        programText.Should().Contain("UseMiddleware<AppAttestationMiddleware>()",
            "middleware must be registered in the ASP.NET Core pipeline");
    }

    [Fact]
    public void AppAttestationMiddleware_RegisteredAfterRateLimiterBeforeAdminSecurity()
    {
        var programText = ReadFile("backend", "Karar.Api", "Program.cs");

        var rateLimiterPos = programText.IndexOf("UseMiddleware<DistributedRateLimitMiddleware>()", StringComparison.Ordinal);
        var attestationPos = programText.IndexOf("UseMiddleware<AppAttestationMiddleware>()", StringComparison.Ordinal);
        var adminPos = programText.IndexOf("UseMiddleware<AdminSecurityMiddleware>()", StringComparison.Ordinal);

        rateLimiterPos.Should().BeGreaterThanOrEqualTo(0, "DistributedRateLimitMiddleware must exist");
        attestationPos.Should().BeGreaterThan(rateLimiterPos,
            "AppAttestationMiddleware must run after rate limiting");
        adminPos.Should().BeGreaterThan(attestationPos,
            "AdminSecurityMiddleware must run after attestation");
    }

    [Fact]
    public void AppAttestationMiddleware_SkipsRequestsWithoutAttribute()
    {
        var middlewareText = ReadFile(
            "backend", "Karar.Api", "Common", "Middleware", "AppAttestationMiddleware.cs");

        middlewareText.Should().Contain("RequireAttestationAttribute",
            "middleware must only activate for attributed endpoints");
        middlewareText.Should().Contain("attr is null",
            "middleware must call next() when attribute is absent");
    }

    [Fact]
    public void AppAttestationMiddleware_SkipsAnonymousDeviceRequests()
    {
        var middlewareText = ReadFile(
            "backend", "Karar.Api", "Common", "Middleware", "AppAttestationMiddleware.cs");

        middlewareText.Should().Contain("deviceId is null",
            "middleware must skip attestation when no device token is present (web/anonymous requests)");
    }

    [Fact]
    public void AppAttestationMiddleware_SoftEnforceByDefault_HardEnforceViaConfig()
    {
        var middlewareText = ReadFile(
            "backend", "Karar.Api", "Common", "Middleware", "AppAttestationMiddleware.cs");
        var serviceText = ReadFile(
            "backend", "Karar.Api", "Services", "AppAttestationService.cs");

        middlewareText.Should().Contain("ShouldBlock",
            "middleware must check the ShouldBlock flag from the attestation decision");
        serviceText.Should().Contain("AppAttestation:HardEnforce:{endpointKey}",
            "hard-enforce must be controlled per-endpoint via config, not hardcoded");
    }

    [Fact]
    public void AppAttestationMiddleware_Returns403OnHardEnforceBlock()
    {
        var middlewareText = ReadFile(
            "backend", "Karar.Api", "Common", "Middleware", "AppAttestationMiddleware.cs");

        middlewareText.Should().Contain("Status403Forbidden",
            "hard-enforce block must return HTTP 403");
        middlewareText.Should().Contain("APP_ATTESTATION_FAILED",
            "error code must match the inline attestation contract");
    }

    [Fact]
    public void MigrationConflict_V46HasSingleUniqueVersion()
    {
        var migrationsDir = TestRepoPaths.FilePath("backend", "migrations");
        var v46Files = Directory.GetFiles(migrationsDir, "V46__*.sql");

        v46Files.Should().HaveCount(1,
            "exactly one V46 migration must exist to avoid Flyway version conflict");
        v46Files[0].Should().Contain("app_attestation_soft_enforce",
            "the sole V46 migration must be the attestation schema");
    }

    [Fact]
    public void AdminScheduledReportsMigration_IsRenamedToV50()
    {
        var migration = ReadFile("backend", "migrations", "V50__admin_scheduled_reports.sql");

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS admin_scheduled_reports",
            "admin_scheduled_reports table must exist in V50 migration");
        migration.Should().Contain("filters     JSONB");
        migration.Should().NotContain("user_id");
        migration.Should().NotContain("device_id");
        migration.Should().NotContain("email");
    }

    private static string ReadFile(params string[] pathParts) =>
        TestRepoPaths.ReadText(pathParts);
}
