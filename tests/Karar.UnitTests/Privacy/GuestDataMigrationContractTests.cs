using FluentAssertions;

namespace Karar.UnitTests.Privacy;

public sealed class GuestDataMigrationContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void Program_MapsDocumentedGuestDataMigrationEndpoint()
    {
        var block = MigrationEndpointBlock();

        block.Should().Contain("X-Device-Token");
        block.Should().Contain("UPDATE posts");
        block.Should().Contain("UPDATE votes");
        block.Should().Contain("UPDATE reports");
        block.Should().Contain("AND u.id = @userId");
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

    // ── Gizlilik: misafir içerik hesaba bağlanırken kamuya açık yazar olmamalı ──────────
    // İstemci migrate-guest-data'yı her login/register/verify sonrasında sessizce çağırır.
    // Önceden yazarsız görünen misafir postları/yorumları kullanıcı adının altında yayınlanıyor,
    // paylaşılan cihazda başkasının içeriği hesap sahibine atfediliyordu.

    [Fact]
    public void MigratedGuestPosts_StayAnonymous()
    {
        var postsUpdate = SliceBlock(MigrationEndpointBlock(), "UPDATE posts", "\"\"\"");

        postsUpdate.Should().Contain("is_anonymous = TRUE",
            "posts written without an author must not start showing the account's username");
    }

    [Fact]
    public void MigratedGuestPosts_AreLimitedToContentCreatedBeforeTheAccount()
    {
        var postsUpdate = SliceBlock(MigrationEndpointBlock(), "UPDATE posts", "\"\"\"");

        postsUpdate.Should().Contain("created_at < (SELECT u.created_at FROM users u WHERE u.id = @userId)",
            "guest content created on the device after registration (shared device, expired token) is not the account's");
    }

    [Fact]
    public void GuestComments_AreNotAttachedToThePublicAuthor()
    {
        MigrationEndpointBlock().Should().NotContain("UPDATE comments",
            "comment threads render u.username for any comment with a user_id; guest comments must stay authorless");
    }

    [Fact]
    public void PublicUserPosts_HideAnonymousPostsFromOtherViewers()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/posts\"",
            "app.MapGet(\"/api/v1/users/me/comments\"");

        block.Should().Contain("AND (is_anonymous = FALSE OR user_id = @viewerId)",
            "the public post count must not include another user's anonymous posts");
        block.Should().Contain("AND (p.is_anonymous = FALSE OR p.user_id = @userParam)",
            "listing anonymous posts under a username de-anonymises them even with the name nulled");
    }

    [Fact]
    public void PublicProfile_PostCountExcludesAnonymousPosts()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/profile\"",
            "app.MapPost(\"/api/v1/auth/2fa/setup\"");

        block.Should().Contain("p.user_id = u.id AND p.status != 'deleted' AND p.is_anonymous = FALSE",
            "a public counter that moves when an anonymous post appears is a de-anonymisation side channel");
    }

    private static string MigrationEndpointBlock() =>
        SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/users/me/migrate-guest-data\"",
            "app.MapGet(\"/api/v1/users/username-availability\"");

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
