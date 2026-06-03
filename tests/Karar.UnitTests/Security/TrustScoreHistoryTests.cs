using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class TrustScoreHistoryTests
{
    [Fact]
    public void Migration_CreatesDeviceTrustScoreHistoryTable()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V52__brigade_coordinated_detection_enforcement.sql");

        migration.Should().Contain("CREATE TABLE IF NOT EXISTS device_trust_score_history", "history table required");
        migration.Should().Contain("device_id   TEXT NOT NULL", "device_id column required");
        migration.Should().Contain("score       FLOAT NOT NULL", "score column required");
        migration.Should().Contain("reason      TEXT", "reason column required");
        migration.Should().Contain("recorded_at TIMESTAMPTZ NOT NULL DEFAULT NOW()", "recorded_at with default required");
    }

    [Fact]
    public void Migration_CreatesIndexOnDeviceIdAndRecordedAt()
    {
        var migration = TestRepoPaths.ReadText("backend", "migrations", "V52__brigade_coordinated_detection_enforcement.sql");

        migration.Should().Contain("idx_device_trust_score_history_device_recorded", "index on device_id + recorded_at required");
    }

    [Fact]
    public void DeviceTrustService_InsertsHistoryOnScoreUpsert()
    {
        var service = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "DeviceTrustService.cs");

        service.Should().Contain("RecordTrustScoreHistoryAsync", "history insert helper must be called in UpsertScoreAsync");
        service.Should().Contain("device_trust_score_history", "must INSERT into device_trust_score_history");
        service.Should().Contain("device_id, score, reason", "history insert must include required columns");
    }

    [Fact]
    public void TrustHistoryEndpoint_Returns90DaysOfHistory()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("/api/v1/admin/devices/{deviceId}/trust-history", "trust history endpoint must exist");
        program.Should().Contain("INTERVAL '90 days'", "endpoint must return last 90 days");
        program.Should().Contain("ORDER BY recorded_at DESC", "history must be ordered newest first");
    }

    [Fact]
    public void TrustHistoryDto_HasRequiredFields()
    {
        var responses = TestRepoPaths.ReadText("backend", "Karar.Api", "Contracts", "Responses.cs");

        responses.Should().Contain("DeviceTrustHistoryDto", "DTO required");
        responses.Should().Contain("double Score", "Score field required");
        responses.Should().Contain("string? Reason", "Reason field required");
        responses.Should().Contain("DateTimeOffset RecordedAt", "RecordedAt field required");
    }
}
