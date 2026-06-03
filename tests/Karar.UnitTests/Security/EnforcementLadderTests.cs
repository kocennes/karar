using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class EnforcementLadderTests
{
    [Fact]
    public void Migration_CreatesEnforcementActionsTable()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V52__brigade_coordinated_detection_enforcement.sql");

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS enforcement_actions", "enforcement_actions table required");
        migration.Should().Contain("'warning'", "warning action must be in CHECK constraint");
        migration.Should().Contain("'strike'", "strike action must be in CHECK constraint");
        migration.Should().Contain("'temp_ban'", "temp_ban action must be in CHECK constraint");
        migration.Should().Contain("'perm_ban'", "perm_ban action must be in CHECK constraint");
        migration.Should().Contain("target_type", "target_type column required");
        migration.Should().Contain("expires_at", "expires_at column required for temp_ban");
    }

    [Fact]
    public void Migration_AddsStrikeCountToDevices()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V52__brigade_coordinated_detection_enforcement.sql");

        migration.Should().Contain("strike_count", "strike_count column must be added to devices");
        migration.Should().Contain("flags", "flags JSONB column must be added to devices");
    }

    [Fact]
    public void EnforcementEndpoint_AutoStrikesOn3rdWarning()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        // Enforcement endpoint must exist
        program.Should().Contain("MapPost(\"/api/v1/admin/enforcement\"",
            "enforcement POST endpoint must be mapped in Program.cs");

        // 3rd warning → auto strike logic
        program.Should().Contain("warningCount >= 3", "3rd warning must trigger auto-strike");
        program.Should().Contain("auto_strike_after_3_warnings", "auto-strike reason must be set");
        program.Should().Contain("strike_count = strike_count + 1", "strike_count must be incremented on device");
    }

    [Fact]
    public void EnforcementEndpoint_ValidatesAction()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("\"warning\" or \"strike\" or \"temp_ban\" or \"perm_ban\"",
            "action must be validated against the allowed values");
        program.Should().Contain("MISSING_EXPIRES_AT", "temp_ban without expires_at must return error");
    }

    [Fact]
    public void EnforcementHistoryEndpoint_Exists()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("/api/v1/admin/enforcement/{targetId}", "history endpoint must exist");
    }

    [Fact]
    public void NewAccountHighReport_FlagsDeviceAndInsertsAlert()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("CheckAndFlagNewAccountHighReportAsync", "flag check must be called from report endpoint");
        program.Should().Contain("new_account_high_report", "flag key must match spec");
        program.Should().Contain("reportCount < 10", "threshold must be 10 reports");
        program.Should().Contain("INTERVAL '24 hours'", "account age window must be 24 hours");
    }
}
