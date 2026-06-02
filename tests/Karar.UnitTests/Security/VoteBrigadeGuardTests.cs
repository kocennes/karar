using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class VoteBrigadeGuardTests
{
    // ── Constants ──────────────────────────────────────────────────────────

    [Fact]
    public void SuppressThreshold_Is5()
    {
        VoteBrigadeGuard.SuppressThreshold.Should().Be(5);
    }

    [Fact]
    public void WindowMinutes_Is10()
    {
        VoteBrigadeGuard.WindowMinutes.Should().Be(10);
    }

    [Fact]
    public void FingerprintPrefixLength_Is8()
    {
        VoteBrigadeGuard.FingerprintPrefixLength.Should().Be(8);
    }

    // ── BrigadeGuardResult ──────────────────────────────────────────────────

    [Fact]
    public void BrigadeGuardResult_None_IsNotDetected()
    {
        BrigadeGuardResult.None.Detected.Should().BeFalse();
        BrigadeGuardResult.None.DeviceCount.Should().Be(0);
        BrigadeGuardResult.None.IpConcentration.Should().Be(0.0);
    }

    [Fact]
    public void BrigadeGuardResult_WithDetection_HasCorrectFields()
    {
        var result = new BrigadeGuardResult(true, 7, 0.85, 42L);
        result.Detected.Should().BeTrue();
        result.DeviceCount.Should().Be(7);
        result.IpConcentration.Should().Be(0.85);
        result.AlertId.Should().Be(42L);
    }

    // ── Service code: IP block detection ───────────────────────────────────

    [Fact]
    public void ServiceCode_CountsDistinctDevicesByIpBlockInWindow()
    {
        var code = ReadGuard();

        code.Should().Contain("COUNT(DISTINCT v.device_id)", "must count distinct devices in IP block");
        code.Should().Contain("v.voter_ip_block = @ipBlock", "must filter by IP block");
        code.Should().Contain("@windowMinutes * INTERVAL '1 minute'", "must use parameterised window");
        code.Should().Contain("v.is_quarantined = FALSE", "must skip already-quarantined votes");
    }

    [Fact]
    public void ServiceCode_QuarantinesIpBlockVotesOnDetection()
    {
        var code = ReadGuard();

        code.Should().Contain("QuarantineByIpBlockAsync", "must call IP block quarantine on detection");
        code.Should().Contain("is_quarantined = TRUE", "quarantine must set is_quarantined flag");
        code.Should().Contain("quarantined     = TRUE", "quarantine must also set quarantined column (brigade job compat)");
    }

    // ── Service code: fingerprint prefix detection ──────────────────────────

    [Fact]
    public void ServiceCode_ChecksFingerprintPrefixCluster()
    {
        var code = ReadGuard();

        code.Should().Contain("LEFT(d.fingerprint, @prefixLen)", "must use left-prefix of device fingerprint");
        code.Should().Contain("FingerprintPrefixLength", "must reference FingerprintPrefixLength constant");
        code.Should().Contain("JOIN devices d ON d.id = v.device_id", "must join devices for fingerprint");
    }

    [Fact]
    public void ServiceCode_QuarantinesByFingerprintPrefixOnDetection()
    {
        var code = ReadGuard();

        code.Should().Contain("QuarantineByFingerprintPrefixAsync", "must call fingerprint quarantine method");
        code.Should().Contain("LEFT(d.fingerprint, @prefixLen) = @fpPrefix", "must match fingerprint prefix in UPDATE");
    }

    // ── Service code: admin alert ───────────────────────────────────────────

    [Fact]
    public void ServiceCode_InsertsAdminAlertWithRequiredFields()
    {
        var code = ReadGuard();

        code.Should().Contain("brigade_inline_suppressed", "alert type must be brigade_inline_suppressed");
        code.Should().Contain("admin_alerts", "must INSERT into admin_alerts table");
        code.Should().Contain("post_id", "alert payload must include post_id");
        code.Should().Contain("device_count", "alert payload must include device_count");
        code.Should().Contain("ip_concentration", "alert payload must include ip_concentration");
        code.Should().Contain("detected_at", "alert payload must include detected_at");
    }

    // ── Program.cs: vote endpoint integration ──────────────────────────────

    [Fact]
    public void VoteEndpoint_IncludesBrigadeGuardInParameters()
    {
        var voteBlock = SliceVoteBlock();

        voteBlock.Should().Contain("VoteBrigadeGuard brigadeGuard",
            "vote endpoint must inject VoteBrigadeGuard");
    }

    [Fact]
    public void VoteEndpoint_CallsCheckAndSuppressAsync()
    {
        var voteBlock = SliceVoteBlock();

        voteBlock.Should().Contain("brigadeGuard.CheckAndSuppressAsync(",
            "vote endpoint must call CheckAndSuppressAsync after upsert");
    }

    [Fact]
    public void VoteEndpoint_SkipsMarkDirtyWhenBrigadeDetected()
    {
        var voteBlock = SliceVoteBlock();

        voteBlock.Should().Contain("!brigadeResult.Detected",
            "MarkPostDirtyAsync must be skipped when brigade is detected");
    }

    [Fact]
    public void VoteEndpoint_LogsBrigadeInlineSuppressedToComplianceLog()
    {
        var voteBlock = SliceVoteBlock();

        voteBlock.Should().Contain("brigade_inline_suppressed",
            "compliance log must record brigade_inline_suppressed event");
    }

    [Fact]
    public void VoteBrigadeGuard_IsRegisteredAsSingleton()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("AddSingleton<VoteBrigadeGuard>()",
            "VoteBrigadeGuard must be registered as a singleton service");
    }

    // ── Migration ──────────────────────────────────────────────────────────

    [Fact]
    public void Migration_V56_CreatesIpBlockBrigadeIndex()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V56__vote_brigade_inline_guard.sql");

        migration.Should().Contain("idx_votes_brigade_ip_window", "IP block window index must be created");
        migration.Should().Contain("voter_ip_block", "index must cover voter_ip_block column");
        migration.Should().Contain("is_quarantined = FALSE", "partial index must exclude already-quarantined votes");
    }

    [Fact]
    public void Migration_V56_CreatesPostTimeWindowIndex()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V56__vote_brigade_inline_guard.sql");

        migration.Should().Contain("idx_votes_brigade_post_time",
            "post-time window index must be created for fingerprint join");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string ReadGuard() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "VoteBrigadeGuard.cs");

    private static string SliceVoteBlock()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var start = program.IndexOf("app.MapPost(\"/api/v1/posts/{id:guid}/vote\"", StringComparison.Ordinal);
        var end   = program.IndexOf("app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"", start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "vote POST endpoint must exist");
        end.Should().BeGreaterThan(start, "vote DELETE endpoint must follow vote POST endpoint");
        return program[start..end];
    }
}
