using FluentAssertions;

namespace Karar.UnitTests.Privacy;

public sealed class GuestDataMigrationContractTests
{
    [Fact]
    public void Program_MapsDocumentedGuestDataMigrationEndpoint()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        programText.Should().Contain("app.MapPost(\"/api/v1/users/me/migrate-guest-data\"");
        programText.Should().Contain("X-Device-Token");
        programText.Should().Contain("UPDATE posts");
        programText.Should().Contain("UPDATE comments");
        programText.Should().Contain("UPDATE votes");
        programText.Should().Contain("UPDATE reports");
        programText.Should().Contain("AND u.id = @userId");
    }

    [Fact]
    public void VoteUserOwnershipMigration_AddsNullableUserIdForGuestMigration()
    {
        var migrationText = TestRepoPaths.ReadText(
            "backend",
            "migrations",
            "V60__vote_user_ownership.sql");

        migrationText.Should().Contain("ADD COLUMN IF NOT EXISTS user_id UUID REFERENCES users(id) ON DELETE SET NULL");
        migrationText.Should().Contain("idx_votes_user_id");
    }
}
