using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Regresyon: /auth/google mevcut kullanıcıyı is_banned / deleted_at bakmadan bulup token
// veriyordu; admin ban'ı (refresh token'ları iptal eder) Google ile yeniden girişle aşılıyordu.
// (Firebase + Postgres olmadan handler çalıştırılamadığı için kaynak sözleşmesi testi.)
public sealed class GoogleSignInBanContractTests
{
    [Fact]
    public void GoogleSignIn_ReadsBanAndDeletionState()
    {
        var block = GoogleSignInBlock();

        block.Should().Contain("SELECT id, username, email, karma, auth_provider, is_banned, deleted_at IS NOT NULL");
    }

    [Fact]
    public void GoogleSignIn_RefusesBannedOrDeletedUsersBeforeIssuingTokens()
    {
        var block = GoogleSignInBlock();
        var tokenIssue = block.IndexOf("jwtService.GenerateAccessToken(", StringComparison.Ordinal);
        var googleIdUpdate = block.IndexOf("UPDATE users SET google_id = @googleId", StringComparison.Ordinal);
        var banned = block.IndexOf("return BadRequest(\"USER_BANNED\"", StringComparison.Ordinal);
        var deleted = block.IndexOf("return BadRequest(\"ACCOUNT_DELETED\"", StringComparison.Ordinal);

        tokenIssue.Should().BeGreaterThan(0);
        googleIdUpdate.Should().BeGreaterThan(0);
        banned.Should().BeGreaterThan(0, "banned users must not receive fresh tokens via Google sign-in");
        deleted.Should().BeGreaterThan(0, "soft-deleted accounts must go through the recovery flow");

        banned.Should().BeLessThan(googleIdUpdate);
        deleted.Should().BeLessThan(googleIdUpdate);
        banned.Should().BeLessThan(tokenIssue);
        deleted.Should().BeLessThan(tokenIssue);
    }

    private static string GoogleSignInBlock()
    {
        var text = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        const string startMarker = "app.MapPost(\"/api/v1/auth/google\",";
        const string endMarker = "app.MapPost(\"/api/v1/auth/google/link\"";
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return text[start..end];
    }
}
