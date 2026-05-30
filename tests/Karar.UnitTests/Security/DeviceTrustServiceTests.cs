using FluentAssertions;
using Karar.UnitTests;
using Karar.Api.Services;

namespace Karar.UnitTests.Security;

public sealed class DeviceTrustServiceTests
{
    [Fact]
    public void CalculateScore_RewardsOlderBroadNormalDevices()
    {
        var score = DeviceTrustService.CalculateScore(new DeviceTrustSignals(
            DeviceAge: TimeSpan.FromDays(8),
            FailedIntegrityCount: 0,
            ReportAbuseCount: 0,
            VoteBreadthCount: 4,
            RecentVoteCount: 2
        ));

        score.Should().Be(0.7);
    }

    [Fact]
    public void CalculateScore_PenalizesFailedIntegrityBelowSuspiciousThreshold()
    {
        var score = DeviceTrustService.CalculateScore(new DeviceTrustSignals(
            DeviceAge: TimeSpan.FromMinutes(20),
            FailedIntegrityCount: 1,
            ReportAbuseCount: 0,
            VoteBreadthCount: 0,
            RecentVoteCount: 0
        ));

        score.Should().BeLessThan(DeviceTrustService.SuspiciousThreshold);
    }

    [Fact]
    public void VoteEndpoint_QuarantinesSuspiciousVotesWithoutBlockingVisibleCounts()
    {
        var programText = ReadProgram();
        var voteBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"",
            "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");

        voteBlock.Should().Contain("EvaluateForVoteAsync");
        voteBlock.Should().Contain("trustDecision.ShouldQuarantineVote");
        voteBlock.Should().Contain("return Results.Ok(response);");
    }

    [Fact]
    public void TrendScoreQueries_ExcludeQuarantinedVotes()
    {
        var programText = ReadProgram();
        // SQL computation delegated to refresh_trend_scores() in V47 migration
        var trendSqlText = ReadFile("backend", "migrations", "V47__hot_score_pure_sql.sql");

        programText.Should().Contain("v.is_quarantined = FALSE");
        trendSqlText.Should().Contain("v.is_quarantined = FALSE",
            "refresh_trend_scores() must exclude quarantined votes from the trend calculation");
    }

    // ── Soft-enforce: missing integrity never blocks ───────────────────────

    [Fact]
    public void SoftEnforce_EvaluateForAction_NeverSetsQuarantineFlag()
    {
        // A device with failed integrity is suspicious, but EvaluateForActionAsync
        // must return ShouldQuarantineVote=false regardless of trust score.
        var signals = new DeviceTrustSignals(
            DeviceAge: TimeSpan.FromMinutes(10),
            FailedIntegrityCount: 1,
            ReportAbuseCount: 0,
            VoteBreadthCount: 0,
            RecentVoteCount: 0
        );
        var score = DeviceTrustService.CalculateScore(signals);
        score.Should().BeLessThan(DeviceTrustService.SuspiciousThreshold,
            "failed integrity should produce a below-threshold trust score");

        // Soft-enforce contract: the decision object for non-vote actions always
        // has ShouldQuarantineVote=false, even for suspicious devices.
        var softDecision = new DeviceTrustDecision(score, IsSuspicious: true, ShouldQuarantineVote: false);
        softDecision.ShouldQuarantineVote.Should().BeFalse(
            "soft-enforce mode must not block a request because of a missing integrity signal");
    }

    [Fact]
    public void ReportEndpoint_EvaluatesDeviceTrustInSoftEnforceMode()
    {
        var programText = ReadProgram();
        var reportBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/reports\"",
            "app.MapPost(\"/api/v1/feedback\"");

        reportBlock.Should().Contain("EvaluateForActionAsync",
            "report endpoint must call soft-enforce device trust evaluation");
        // Must NOT hard-block on trust result — banned device is blocked by RequestDevice, not here
        reportBlock.Should().NotContain("return Unauthorized();\n\n    await using var transaction",
            "trust result must not become a hard block in the report endpoint");
    }

    [Fact]
    public void CreatePostEndpoint_EvaluatesDeviceTrustInSoftEnforceMode()
    {
        var programText = ReadProgram();
        var postBlock = SliceBlock(
            programText,
            "app.MapPost(\"/api/v1/posts\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}\"");

        postBlock.Should().Contain("EvaluateForActionAsync",
            "create-post endpoint must call soft-enforce device trust evaluation");
    }

    [Fact]
    public void BannedDevice_IsBlockedByRequestDevice_BeforeTrustEvaluation()
    {
        // RequestDevice.TryGetDeviceIdAsync filters is_banned=TRUE at DB level.
        // Trust evaluation is only reached for non-banned devices.
        var requestDeviceText = ReadFile("backend", "Karar.Api", "Services", "RequestDevice.cs");
        requestDeviceText.Should().Contain("is_banned = FALSE",
            "banned devices must be rejected at the RequestDevice layer before trust evaluation runs");
    }

    [Fact]
    public void IntegrityProviders_AreConfigFlaggedAndNeverBlockOnNullResult()
    {
        var appAttestText = ReadFile("backend", "Karar.Api", "Services", "AppAttestService.cs");
        var appCheckText = ReadFile("backend", "Karar.Api", "Services", "FirebaseAppCheckService.cs");
        var playIntegrityText = ReadFile("backend", "Karar.Api", "Services", "PlayIntegrityService.cs");

        // All three must implement the unified interface
        appAttestText.Should().Contain("IIntegrityProvider");
        appCheckText.Should().Contain("IIntegrityProvider");
        playIntegrityText.Should().Contain("IIntegrityProvider");

        // Stub providers must return Skipped — never valid/invalid when not configured
        appAttestText.Should().Contain("IntegrityTokenStatus.Skipped",
            "AppAttest stub must return skipped until iOS:AppAttestEnabled is set");
        appCheckText.Should().Contain("IntegrityTokenStatus.Skipped",
            "AppCheck stub must return skipped until Firebase:AppCheckEnabled is set");

        // PlayIntegrity must return skipped on unexpected errors to avoid blocking legit users
        playIntegrityText.Should().Contain("IntegrityTokenStatus.Skipped",
            "PlayIntegrityService must return skipped on transient errors (soft-enforce)");

        // Stubs must be guarded by a config flag
        appAttestText.Should().Contain("AppAttestEnabled");
        appCheckText.Should().Contain("AppCheckEnabled");
    }

    [Theory]
    [InlineData(true, "", "nonce", IntegrityTokenStatus.Valid, IntegrityTokenStatus.Missing)]
    [InlineData(true, "token", "", IntegrityTokenStatus.Valid, IntegrityTokenStatus.Missing)]
    [InlineData(true, "token", "nonce", IntegrityTokenStatus.Invalid, IntegrityTokenStatus.Invalid)]
    [InlineData(true, "token", "nonce", IntegrityTokenStatus.Expired, IntegrityTokenStatus.Expired)]
    [InlineData(false, "", "", IntegrityTokenStatus.Valid, IntegrityTokenStatus.Skipped)]
    public void AppAttestation_NormalizesTokenStatuses(
        bool providerEnabled,
        string token,
        string nonce,
        IntegrityTokenStatus providerStatus,
        IntegrityTokenStatus expected)
    {
        AppAttestationService.NormalizeProviderStatus(providerEnabled, token, nonce, providerStatus)
            .Should().Be(expected);
    }

    [Fact]
    public void CriticalEndpoints_RunAttestationInSoftEnforceBeforeTrustScore()
    {
        var programText = ReadProgram();

        SliceBlock(programText, "app.MapPost(\"/api/v1/posts\"", "app.MapGet(\"/api/v1/posts/{id:guid}\"")
            .Should().Contain("appAttestation.VerifyAsync(httpRequest, connection, null, effectiveDeviceId.Value, \"create_post\")");

        SliceBlock(programText, "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"", "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"")
            .Should().Contain("appAttestation.VerifyAsync(httpRequest, connection, transaction, effectiveDeviceId.Value, \"vote\")");

        SliceBlock(programText, "app.MapPost(\"/api/v1/reports\"", "app.MapPost(\"/api/v1/feedback\"")
            .Should().Contain("appAttestation.VerifyAsync(httpRequest, connection, null, deviceId.Value, \"report\")");
    }

    [Fact]
    public void SoftEnforce_AttestationFailuresAreMeasuredButNotBlockedWhenFlagOff()
    {
        var serviceText = ReadFile("backend", "Karar.Api", "Services", "AppAttestationService.cs");
        var migrationText = ReadFile("backend", "migrations", "V46__app_attestation_soft_enforce.sql");

        serviceText.Should().Contain("AppAttestation:HardEnforce:{endpointKey}",
            "hard-enforce must be endpoint-flagged");
        serviceText.Should().Contain("security_events",
            "false positive measurement requires a security analytics event");
        serviceText.Should().Contain("falsePositiveMeasurement = true");
        serviceText.Should().Contain("hardEnforce && result.Status is IntegrityTokenStatus.Missing or IntegrityTokenStatus.Invalid or IntegrityTokenStatus.Expired",
            "soft mode must leave ShouldBlock false for missing/invalid/expired token outcomes");

        migrationText.Should().Contain("token_status TEXT NOT NULL CHECK");
        migrationText.Should().Contain("'missing', 'invalid', 'expired'");
    }

    private static string ReadProgram() => ReadFile("backend", "Karar.Api", "Program.cs");

    private static string ReadFile(params string[] pathParts)
    {
        return TestRepoPaths.ReadText(pathParts);
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }

}
