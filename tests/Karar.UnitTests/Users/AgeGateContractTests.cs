using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.Api.Contracts;
using Karar.Api.Data;
using Karar.Api.Services;
using Karar.UnitTests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Karar.UnitTests.Users;

// P0-02: temporary neutral 18+ age gate on every sign-up path.
public sealed class AgeGateContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly string MigrationText =
        TestRepoPaths.ReadText("backend", "migrations", "V95__age_gate_neutral.sql");

    // ── Age decision ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2008-10-28", "2026-10-28", AgeGateDecision.Eligible)]     // 18th birthday today
    [InlineData("2008-10-29", "2026-10-28", AgeGateDecision.NotEligible)]  // 18 tomorrow
    [InlineData("2012-05-01", "2026-10-28", AgeGateDecision.NotEligible)]
    [InlineData("1990-01-01", "2026-10-28", AgeGateDecision.Eligible)]
    [InlineData("2008-02-29", "2026-02-28", AgeGateDecision.NotEligible)]  // leap-day birthday
    [InlineData("2008-02-29", "2026-03-01", AgeGateDecision.Eligible)]
    [InlineData("2026-10-29", "2026-10-28", AgeGateDecision.InvalidDate)]  // future date
    [InlineData("1900-01-01", "2026-10-28", AgeGateDecision.InvalidDate)]  // implausible
    [InlineData("0001-01-01", "2026-10-28", AgeGateDecision.InvalidDate)]  // missing DateTime → default
    public void Evaluate_ClassifiesBirthDates(string dob, string today, AgeGateDecision expected)
    {
        AgeGateService.Evaluate(DateOnly.Parse(dob), DateOnly.Parse(today)).Should().Be(expected);
    }

    [Fact]
    public void TodayInTurkey_UsesTurkishCalendarDate()
    {
        // 22:30 UTC on 31 Oct is already 1 Nov in Türkiye (UTC+3).
        AgeGateService.TodayInTurkey(new DateTimeOffset(2026, 10, 31, 22, 30, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 11, 1));
        AgeGateService.TodayInTurkey(new DateTimeOffset(2026, 10, 31, 20, 59, 0, TimeSpan.Zero))
            .Should().Be(new DateOnly(2026, 10, 31));
    }

    [Fact]
    public void RefusalCopy_IsNeutral()
    {
        foreach (var message in new[]
                 {
                     AgeGateCodes.NotEligibleMessage,
                     AgeGateCodes.VerificationRequiredMessage,
                     AgeGateCodes.DeclarationRequiredMessage,
                 })
        {
            message.Should().NotMatchRegex(@"\d", "the threshold must never be shown");
            message.ToLowerInvariant().Should().NotContain("yaş", "the copy must not talk about an age limit");
            message.ToLowerInvariant().Should().NotContain("tekrar", "a refusal must not invite another attempt");
            message.ToLowerInvariant().Should().NotContain("başka", "a refusal must not suggest another date");
        }
    }

    // ── Flags ────────────────────────────────────────────────────────────────

    [Fact]
    public void Flags_DefaultToFalse_InEveryEnvironment()
    {
        var service = CreateService(new Dictionary<string, string?>());
        service.Enforce.Should().BeFalse();
        service.GuestVote.Should().BeFalse();

        var appsettings = JsonDocument.Parse(TestRepoPaths.ReadText("backend", "Karar.Api", "appsettings.json"));
        var ageGate = appsettings.RootElement.GetProperty("AgeGate");
        ageGate.GetProperty("Enforce").GetBoolean().Should().BeFalse();
        ageGate.GetProperty("GuestVote").GetBoolean().Should().BeFalse();

        foreach (var file in Directory.GetFiles(TestRepoPaths.FilePath("backend", "Karar.Api"), "appsettings.*.json"))
        {
            File.ReadAllText(file).Should().NotContain("AgeGate",
                because: $"{Path.GetFileName(file)} must not switch the gate on; the owner flips it via env vars");
        }
    }

    [Fact]
    public async Task CheckWrite_WithFlagsOff_PassesWithoutTouchingTheDatabase()
    {
        // The connection string points at a closed port: any DB access would throw.
        var service = CreateService(new Dictionary<string, string?>());
        var request = new DefaultHttpContext().Request;
        request.Headers["X-Device-Token"] = "device-token";

        (await service.CheckWriteAsync(request, AgeGateScope.UserWrite)).Should().BeNull();
        (await service.CheckWriteAsync(request, AgeGateScope.Vote)).Should().BeNull();
    }

    [Fact]
    public async Task CheckWrite_EnforceOnly_LeavesGuestsAlone()
    {
        var service = CreateService(new Dictionary<string, string?> { ["AgeGate:Enforce"] = "true" });
        var request = new DefaultHttpContext().Request;
        request.Headers["X-Device-Token"] = "device-token";

        (await service.CheckWriteAsync(request, AgeGateScope.Vote)).Should().BeNull(
            "guest votes are governed by AgeGate:GuestVote, not AgeGate:Enforce");
    }

    [Fact]
    public async Task CheckWrite_GuestVoteOn_WithoutDeviceHeader_LeavesTheEndpointToAnswer401()
    {
        var service = CreateService(new Dictionary<string, string?> { ["AgeGate:GuestVote"] = "true" });
        var request = new DefaultHttpContext().Request;

        (await service.CheckWriteAsync(request, AgeGateScope.Vote)).Should().BeNull();
        (await service.CheckWriteAsync(request, AgeGateScope.UserWrite)).Should().BeNull(
            "GuestVote only gates post votes");
    }

    // ── JSON contract (camelCase, as the Flutter client reads it) ────────────

    [Fact]
    public void AuthTokensResponse_SerializesRequiresOnboarding_InCamelCase_AndOmitsItWhenUnknown()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var withFlag = new AuthTokensResponse(Guid.NewGuid(), "kararci", "a", "r", DateTimeOffset.UtcNow,
            User: new UserProfile(Guid.NewGuid(), "kararci", "k@example.com", 0, "google", true),
            RequiresOnboarding: true);
        var json = JsonSerializer.Serialize(withFlag, options);
        json.Should().Contain("\"requiresOnboarding\":true");
        Regex.Matches(json, "requiresOnboarding").Count.Should().Be(1,
            "the nested user profile must not carry a second, default value");

        var refresh = new AuthTokensResponse(Guid.NewGuid(), "kararci", "a", "r", DateTimeOffset.UtcNow);
        JsonSerializer.Serialize(refresh, options).Should().NotContain("requiresOnboarding");

        var publicProfile = new UserProfile(Guid.NewGuid(), "kararci", "k@example.com", 0, "password", true);
        JsonSerializer.Serialize(publicProfile, options).Should().NotContain("requiresOnboarding",
            "public profiles must not reveal whether an account declared a birth date");

        JsonSerializer.Serialize(new OnboardingStatusResponse(false), options)
            .Should().Be("{\"requiresOnboarding\":false}");
    }

    // ── Sign-up paths (source contracts) ─────────────────────────────────────

    [Fact]
    public void GoogleSignIn_FlagsAccountsWithoutBirthDate()
    {
        var block = Slice("app.MapPost(\"/api/v1/auth/google\"", "app.MapPost(\"/api/v1/auth/google/link\"");

        block.Should().Contain("SELECT id, username, email, karma, auth_provider, date_of_birth IS NULL");
        block.Should().Contain("requiresOnboarding = true;", "a brand-new Google account has no birth date");
        block.Should().Contain("requiresOnboarding = true\n", "the 201 body for new users carries the flag");
        block.Should().Contain("RequiresOnboarding: requiresOnboarding", "returning users without a birth date too");
    }

    [Fact]
    public void Login_VerifyEmail_AndMe_ExposeRequiresOnboarding()
    {
        Slice("app.MapPost(\"/api/v1/auth/login\"", "app.MapPost(\"/api/v1/auth/refresh\"")
            .Should().Contain("date_of_birth IS NULL").And.Contain("RequiresOnboarding: requiresOnboarding");
        Slice("app.MapPost(\"/api/v1/auth/verify-email\"", "app.MapPost(\"/api/v1/auth/resend-otp\"")
            .Should().Contain("date_of_birth IS NULL").And.Contain("RequiresOnboarding: requiresOnboarding");
        Slice("app.MapGet(\"/api/v1/users/me\"", "app.MapPost(\"/api/v1/users/me/migrate-guest-data\"")
            .Should().Contain("u.date_of_birth IS NULL AS requires_onboarding")
            .And.Contain("RequiresOnboarding: reader.GetBoolean(12)");
    }

    [Fact]
    public void Register_RefusesNeutrally_AndLocksTheDevice()
    {
        var block = Slice("app.MapPost(\"/api/v1/auth/register\"", "app.MapPost(\"/api/v1/auth/verify-email\"");

        block.Should().Contain("AgeGateService.Evaluate(dob, AgeGateService.TodayInTurkey())");
        block.Should().Contain("AgeGateService.IsDeviceLockedAsync(connection, deviceId.Value)");
        block.Should().Contain("AgeGateService.MarkDeviceFailedAsync(connection, deviceId.Value)");
        block.Should().Contain("AgeGateService.NotEligibleResult()");
        block.Should().NotContain("UNDER_AGE");
        block.Should().NotContain("AGE_CONFIRMATION_REQUIRED", "the separate 18+ checkbox is gone");
        block.Should().NotContain("18 yaş");

        block.IndexOf("MarkDeviceFailedAsync", StringComparison.Ordinal).Should().BeLessThan(
            block.IndexOf("INSERT INTO users", StringComparison.Ordinal),
            "an ineligible registration must stop before anything is written to users");
    }

    [Fact]
    public void Onboarding_SetsBirthDateOnce_AndNeverStoresAnIneligibleDate()
    {
        var block = Slice("app.MapPost(\"/api/v1/users/me/onboarding\"", "app.MapGet(\"/api/v1/users/me/data-export\"");

        block.Should().Contain("GetJwtPrincipal(httpRequest, jwtService)");
        block.Should().Contain("POLICY_ACCEPTANCE_REQUIRED");
        block.Should().Contain("FOR UPDATE");
        block.Should().Contain("AND date_of_birth IS NULL", "an existing birth date is never overwritten");
        block.Should().Contain("AgeGateService.IsDeviceLockedAsync(connection, deviceId, transaction)");
        block.Should().Contain("UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = @id AND revoked_at IS NULL");

        var refusal = block.IndexOf("if (ageDecision == AgeGateDecision.NotEligible)", StringComparison.Ordinal);
        var write = block.IndexOf("SET date_of_birth", StringComparison.Ordinal);
        refusal.Should().BeGreaterThan(0);
        write.Should().BeGreaterThan(refusal, "the ineligible branch returns before date_of_birth is written");
        block[refusal..write].Should().Contain("MarkDeviceFailedAsync").And.Contain("return AgeGateService.NotEligibleResult();");
    }

    [Fact]
    public void DeviceAgeDeclaration_StoresOnlyTimestamps()
    {
        var block = Slice("app.MapPost(\"/api/v1/devices/age-declaration\"", "app.MapGet(\"/api/v1/moderation/transparency\"");

        block.Should().Contain("requestDevice.TryGetDeviceIdAsync(httpRequest)");
        block.Should().Contain("MarkDeviceFailedAsync").And.Contain("MarkDeviceDeclaredAsync");
        block.Should().NotContain("date_of_birth", "a guest's declared date is never stored");
    }

    // ── Enforcement on write endpoints ───────────────────────────────────────

    [Theory]
    [InlineData("app.MapPost(\"/api/v1/posts\", async (", ".RequireAgeGate();")]
    [InlineData("app.MapPost(\"/api/v1/posts/{id:guid}/vote\", async (", ".RequireAgeGate(AgeGateScope.Vote);")]
    [InlineData("app.MapPost(\"/api/v1/posts/{id:guid}/comments\", async (", ".RequireAgeGate();")]
    [InlineData("app.MapPost(\"/api/v1/comments/{id:guid}/reactions\", async (", ".RequireAgeGate();")]
    [InlineData("app.MapPost(\"/api/v1/comments/{id:guid}/upvote\", async (", ".RequireAgeGate();")]
    [InlineData("app.MapPost(\"/api/v1/comments/{id:guid}/downvote\", async (", ".RequireAgeGate();")]
    public void WriteEndpoints_CarryTheAgeGate(string startMarker, string gate)
    {
        EndpointBlock(startMarker).Should().Contain(gate);
    }

    [Fact]
    public void Reports_StayOpen_SoAMinorCanAlwaysBeReported()
    {
        EndpointBlock("app.MapPost(\"/api/v1/reports\", async (").Should().NotContain("RequireAgeGate");
    }

    [Fact]
    public void AgeGateFilter_RefusesSignedInUsersWithoutBirthDate_WithNeutral403()
    {
        var source = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "AgeGateService.cs");
        source.Should().Contain("SELECT date_of_birth IS NULL FROM users WHERE id = @userId AND deleted_at IS NULL");
        source.Should().Contain("AgeGateCodes.VerificationRequired");
        source.Should().Contain("StatusCodes.Status403Forbidden");
        AgeGateCodes.VerificationRequired.Should().Be("AGE_VERIFICATION_REQUIRED");
        AgeGateCodes.DeclarationRequired.Should().Be("AGE_DECLARATION_REQUIRED");
        AgeGateCodes.NotEligible.Should().Be("AGE_NOT_ELIGIBLE");
    }

    // ── Migration ────────────────────────────────────────────────────────────

    [Fact]
    public void Migration_AddsIdempotentDeviceColumns()
    {
        MigrationText.Should().Contain("ADD COLUMN IF NOT EXISTS age_gate_failed_at TIMESTAMPTZ");
        MigrationText.Should().Contain("ADD COLUMN IF NOT EXISTS age_declared_at    TIMESTAMPTZ");
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "AgeGateService.cs")
            .Should().Contain("SET age_gate_failed_at = COALESCE(age_gate_failed_at, NOW())")
            .And.Contain("SET age_declared_at = COALESCE(age_declared_at, NOW())");
    }

    [Fact]
    public void Migration_ReportReasonCheck_EqualsAllowlist_AndCoversV1AndV65()
    {
        var checkReasons = Regex.Matches(
                MigrationText[MigrationText.IndexOf("CHECK (reason IN (", StringComparison.Ordinal)..],
                "'([a-z_]+)'")
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var allowlistLine = ProgramText.Split('\n').Single(l => l.Contains("if (request.Reason is not ("));
        var allowlist = Regex.Matches(allowlistLine, "\"([a-z_]+)\"").Select(m => m.Groups[1].Value).ToHashSet();

        checkReasons.Should().BeEquivalentTo(allowlist, "a reason the API accepts must not violate the DB CHECK (500)");
        checkReasons.Should().Contain("minor");

        string[] v1 = ["hate_speech", "harassment", "personal_info", "spam", "self_harm", "illegal", "other"];
        string[] v65 = [.. v1, "misinformation"]; // PR #19
        checkReasons.Should().Contain(v65, "V95 must be a superset of V1 and V65 so merge order does not matter");
    }

    [Fact]
    public void Migration_NumberIsUniqueAndInTheProductRange()
    {
        var versions = Directory.GetFiles(TestRepoPaths.FilePath("backend", "migrations"), "V*.sql")
            .Select(Path.GetFileName)
            .Where(name => name!.StartsWith("V95__", StringComparison.Ordinal))
            .ToArray();
        versions.Should().ContainSingle().Which.Should().Be("V95__age_gate_neutral.sql");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static AgeGateService CreateService(Dictionary<string, string?> values)
    {
        var settings = new Dictionary<string, string?>(values)
        {
            ["ConnectionStrings:Postgres"] = "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none;Timeout=1",
            ["Jwt:Secret"] = "unit-test-secret-minimum-32-characters-long",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new AgeGateService(configuration, new Db(configuration), new JwtService(configuration));
    }

    private static string Slice(string startMarker, string endMarker)
    {
        var text = ProgramText.Replace("\r\n", "\n");
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }

    // From the MapPost line up to the next top-level app.Map call (the endpoint's own chain).
    private static string EndpointBlock(string startMarker)
    {
        var text = ProgramText.Replace("\r\n", "\n");
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf("\napp.Map", start + startMarker.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }
}
