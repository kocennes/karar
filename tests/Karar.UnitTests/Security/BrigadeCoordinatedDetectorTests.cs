using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class BrigadeCoordinatedDetectorTests
{
    [Fact]
    public void Job_Interval_IsTenMinutes()
    {
        BrigadeCoordinatedDetectorJob.Interval.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void MinDeviceThreshold_Is15()
    {
        BrigadeCoordinatedDetectorJob.MinDevices.Should().Be(15);
    }

    [Fact]
    public void IpConcentrationThreshold_Is60Percent()
    {
        BrigadeCoordinatedDetectorJob.IpBlockConcentrationThreshold.Should().Be(0.60);
    }

    [Fact]
    public void YoungAccountMaxHours_Is72()
    {
        BrigadeCoordinatedDetectorJob.YoungAccountMaxHours.Should().Be(72);
    }

    [Fact]
    public void VoteWindowMinutes_Is6()
    {
        BrigadeCoordinatedDetectorJob.VoteWindowMinutes.Should().Be(6);
    }

    [Fact]
    public void JobCode_QueriesVoteWindowAndQuarantinesOnDetection()
    {
        var job = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "BrigadeCoordinatedDetectorJob.cs");

        // Detection criteria
        job.Should().Contain("INTERVAL '6 minutes'", "vote window must be 6 minutes");
        job.Should().Contain("COUNT(DISTINCT device_id)", "must count distinct devices");
        job.Should().Contain("median_account_age_hours < @youngAccountMaxHours", "median age filter must be present");
        job.Should().Contain("device_count >= @minDevices", "must check ≥15 devices");
        job.Should().Contain("ip_concentration", "must compute IP block concentration");
        job.Should().Contain("fp_concentration", "must compute fingerprint prefix concentration");
        job.Should().Contain("IpBlockConcentrationThreshold", "must apply 60% concentration threshold");
        job.Should().Contain("INTERVAL '24 hours'", "coordinated pattern check must look at 24h window");

        // Quarantine
        job.Should().Contain("quarantined = TRUE", "detected votes must be quarantined");

        // Admin alert
        job.Should().Contain("brigade_suspected", "brigade alert type must be inserted");
        job.Should().Contain("admin_alerts", "must INSERT into admin_alerts table");
    }

    [Fact]
    public void JobCode_IsRegisteredAsHostedService()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        program.Should().Contain("AddHostedService<BrigadeCoordinatedDetectorJob>",
            "BrigadeCoordinatedDetectorJob must be registered as a hosted service");
    }

    [Fact]
    public void Migration_CreatesAdminAlertsTable()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V52__brigade_coordinated_detection_enforcement.sql");

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS admin_alerts", "admin_alerts table required");
        migration.Should().Contain("type         TEXT NOT NULL", "type column required");
        migration.Should().Contain("payload      JSONB", "JSONB payload required");
        migration.Should().Contain("is_resolved  BOOLEAN", "is_resolved flag required");
    }

    [Fact]
    public void Migration_CreatesQuarantinedColumn()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V52__brigade_coordinated_detection_enforcement.sql");

        migration.Should().Contain("quarantined BOOLEAN", "votes.quarantined column required");
    }
}
