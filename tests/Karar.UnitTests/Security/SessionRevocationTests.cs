using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Regresyon: şifre sıfırlama/değiştirme ve e-posta değişikliği açık refresh token'ları iptal
// etmiyordu (saldırganın oturumu 30 gün kayan pencereyle sürüyordu); /auth/refresh iptal edilmiş
// token'ın tekrar sunulmasını (reuse) tespit etmiyordu ve rotasyon satırı kilitlemeden yapılıyordu.
public sealed class SessionRevocationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    // ── Davranışsal: RefreshTokenRotationPolicy ─────────────────────────────

    [Fact]
    public void ActiveToken_IsRotated()
    {
        RefreshTokenRotationPolicy.Classify(revokedAt: null, rotatedAt: null, familyHasActiveToken: true, Now)
            .Should().Be(RefreshTokenRotationPolicy.Decision.Rotate);
    }

    [Fact]
    public void RotatedTokenReplayedLater_RevokesFamily()
    {
        var rotated = Now.AddMinutes(-10);

        RefreshTokenRotationPolicy.Classify(rotated, rotated, familyHasActiveToken: true, Now)
            .Should().Be(RefreshTokenRotationPolicy.Decision.ReuseDetected);
    }

    [Fact]
    public void ConcurrentRefreshWithSameToken_IsTolerated()
    {
        // İstemci refresh'i single-flight değil: aynı token birkaç saniye arayla tekrar gelebilir.
        var rotated = Now.AddSeconds(-2);

        RefreshTokenRotationPolicy.Classify(rotated, rotated, familyHasActiveToken: true, Now)
            .Should().Be(RefreshTokenRotationPolicy.Decision.ConcurrentGrace);
        RefreshTokenRotationPolicy.Classify(rotated, rotated, true, rotated + RefreshTokenRotationPolicy.ConcurrentRefreshGrace)
            .Should().Be(RefreshTokenRotationPolicy.Decision.ConcurrentGrace);
        RefreshTokenRotationPolicy.Classify(rotated, rotated, true, rotated + RefreshTokenRotationPolicy.ConcurrentRefreshGrace + TimeSpan.FromSeconds(1))
            .Should().Be(RefreshTokenRotationPolicy.Decision.ReuseDetected);
    }

    [Fact]
    public void GraceWindow_CannotResurrectFamilyClosedByCredentialChange()
    {
        // Rotasyondan saniyeler sonra şifre değişti ve tüm oturumlar kapatıldı: eski token yeniden
        // sunulsa bile yeni token verilmemeli.
        var rotated = Now.AddSeconds(-3);

        RefreshTokenRotationPolicy.Classify(rotated, rotated, familyHasActiveToken: false, Now)
            .Should().Be(RefreshTokenRotationPolicy.Decision.Reject);
    }

    [Fact]
    public void LoggedOutOrAdminRevokedToken_IsRejectedWithoutFamilyRevocation()
    {
        RefreshTokenRotationPolicy.Classify(Now.AddMinutes(-1), rotatedAt: null, familyHasActiveToken: true, Now)
            .Should().Be(RefreshTokenRotationPolicy.Decision.Reject);
    }

    // ── Kaynak sözleşmesi: oturum iptali (Postgres gerektirir) ──────────────

    [Fact]
    public void ResetPassword_RevokesAllSessionsOfTheAccount()
    {
        var block = Slice("app.MapPost(\"/api/v1/auth/reset-password\"", "app.MapGet(\"/api/v1/admin/analytics/cache\"");

        block.Should().Contain("AND deleted_at IS NULL RETURNING id");
        AssertBefore(block, "RevokeUserRefreshTokensAsync(connection, transaction, resetUserId)", "await transaction.CommitAsync();");
    }

    [Fact]
    public void ChangePassword_RevokesOtherSessions()
    {
        var block = Slice("app.MapPut(\"/api/v1/users/me/password\"", "// ── FORGOT PASSWORD");

        AssertBefore(block, "RevokeUserRefreshTokensAsync(connection, transaction, userId, request.RefreshToken)",
            "await transaction.CommitAsync();");
    }

    [Fact]
    public void ChangeEmailConfirm_RevokesOtherSessions()
    {
        var block = Slice("app.MapPost(\"/api/v1/auth/change-email/confirm\"", "app.MapPost(\"/api/v1/auth/recover-account\"");

        AssertBefore(block, "RevokeUserRefreshTokensAsync(connection, tx, userId, request.RefreshToken)",
            "await tx.CommitAsync();");
    }

    [Fact]
    public void RevokeHelper_RevokesEveryActiveTokenOfTheUser()
    {
        var helper = Slice("static async Task RevokeUserRefreshTokensAsync(", "await command.ExecuteNonQueryAsync();");

        helper.Should().Contain("UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = @userId AND revoked_at IS NULL");
        helper.Should().Contain(" AND token_hash <> @keepHash");
    }

    // ── Kaynak sözleşmesi: /auth/refresh rotasyonu ──────────────────────────

    [Fact]
    public void Refresh_LocksRowAndRotatesAtomically()
    {
        var block = RefreshBlock();

        block.Should().Contain("FOR UPDATE OF rt");
        block.Should().NotContain("WHERE rt.token_hash = @hash AND rt.revoked_at IS NULL",
            "revoked tokens must be visible to detect reuse");
        block.Should().Contain("UPDATE refresh_tokens SET revoked_at = NOW(), rotated_at = NOW() WHERE id = @id AND revoked_at IS NULL");
        block.Should().Contain("await revokeCmd.ExecuteNonQueryAsync() != 1");
    }

    [Fact]
    public void Refresh_RevokesWholeFamilyOnReuse()
    {
        var block = RefreshBlock();

        block.Should().Contain("RefreshTokenRotationPolicy.Classify(revokedAt, rotatedAt, familyHasActiveToken, dbNow)");
        AssertBefore(block,
            "UPDATE refresh_tokens SET revoked_at = NOW() WHERE user_id = @userId AND (id = @familyId OR family_id = @familyId) AND revoked_at IS NULL",
            "jwtService.GenerateAccessToken(");
        block.Should().Contain("INSERT INTO refresh_tokens (user_id, token_hash, family_id) VALUES (@userId, @hash, @familyId)");
    }

    [Fact]
    public void Migration_AddsFamilyAndRotationColumns()
    {
        var sql = TestRepoPaths.ReadText("backend", "migrations", "V116__refresh_token_reuse_detection.sql");

        sql.Should().Contain("ADD COLUMN IF NOT EXISTS family_id  UUID");
        sql.Should().Contain("ADD COLUMN IF NOT EXISTS rotated_at TIMESTAMPTZ");
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────

    private static string RefreshBlock() =>
        Slice("app.MapPost(\"/api/v1/auth/refresh\"", "app.MapPost(\"/api/v1/auth/logout\"");

    private static string Slice(string startMarker, string endMarker)
    {
        var text = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..(end + endMarker.Length)];
    }

    private static void AssertBefore(string block, string first, string second)
    {
        var a = block.IndexOf(first, StringComparison.Ordinal);
        var b = block.IndexOf(second, StringComparison.Ordinal);
        a.Should().BeGreaterThan(0, $"'{first}' should be present");
        b.Should().BeGreaterThan(0, $"'{second}' should be present");
        a.Should().BeLessThan(b, $"'{first}' must run before '{second}'");
    }
}
