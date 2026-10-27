using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Regresyon: 2FA'yı yalnızca geçerli bir oturum (JWT) ile kapatmak / atlatmak mümkün olmamalı.
// Uç noktalar Program.cs içinde satır içi SQL ile yazıldığından (Postgres olmadan çalıştırılamaz)
// burada kaynak sözleşmesi doğrulanır; karar mantığı AuthAccountPolicyTests'te davranışsal test edilir.
public sealed class TwoFactorDisableContractTests
{
    [Fact]
    public void Disable2fa_VerifiesPasswordAndSecondFactorBeforeClearingSecret()
    {
        var block = SliceEndpointBlock(
            ReadProgram(),
            "app.MapPost(\"/api/v1/auth/2fa/disable\"",
            "app.MapGet(\"/api/v1/users/me/sessions\"");

        var proofCheck = block.IndexOf("AuthAccountPolicy.CheckTwoFactorDisable(", StringComparison.Ordinal);
        var clear = block.IndexOf("is_2fa_enabled = FALSE", StringComparison.Ordinal);

        proofCheck.Should().BeGreaterThanOrEqualTo(0,
            "2FA kapatma yalnızca JWT ile değil, şifre + TOTP/yedek kod ile doğrulanmalı");
        clear.Should().BeGreaterThan(proofCheck, "doğrulama, 2FA sırrı silinmeden önce yapılmalı");

        block.Should().Contain("ReadOptionalJsonRequestAsync<TwoFactorDisableRequest>(");
        block.Should().Contain("password_hash");
        block.Should().Contain("totp_secret");
        block.Should().Contain("PASSWORD_REQUIRED");
        block.Should().Contain("INVALID_PASSWORD");
        block.Should().Contain("TWO_FACTOR_REQUIRED");
        block.Should().Contain("INVALID_TOTP_CODE");
        block.Should().Contain("INVALID_BACKUP_CODE");
        block.Should().Contain("DELETE FROM totp_backup_codes WHERE user_id = @id");
        block.Should().Contain("RequireRateLimiting(\"auth-strict\")");
    }

    [Fact]
    public void Disable2fa_JwtCheckStillRunsBeforeBodyParsing()
    {
        // Misafir isteği (JWT yok) gövdeden bağımsız olarak 401 almalı (AccessControlTests sözleşmesi).
        var block = SliceEndpointBlock(
            ReadProgram(),
            "app.MapPost(\"/api/v1/auth/2fa/disable\"",
            "app.MapGet(\"/api/v1/users/me/sessions\"");

        var jwt = block.IndexOf("GetJwtPrincipal(httpRequest, jwtService)", StringComparison.Ordinal);
        var body = block.IndexOf("ReadOptionalJsonRequestAsync<TwoFactorDisableRequest>(", StringComparison.Ordinal);

        jwt.Should().BeGreaterThanOrEqualTo(0);
        body.Should().BeGreaterThan(jwt);
    }

    [Fact]
    public void Setup2fa_RefusesToReplaceSecretWhileTwoFactorIsEnabled()
    {
        // Aksi halde oturum sahibi yeni sır üretip 2FA'yı kendi cihazına taşıyabilir (kapatmakla eşdeğer).
        var block = SliceEndpointBlock(
            ReadProgram(),
            "app.MapPost(\"/api/v1/auth/2fa/setup\"",
            "app.MapPost(\"/api/v1/auth/2fa/enable\"");

        block.Should().Contain("is_2fa_enabled");
        block.Should().Contain("TWO_FACTOR_ALREADY_ENABLED");

        var guard = block.IndexOf("TWO_FACTOR_ALREADY_ENABLED", StringComparison.Ordinal);
        var overwrite = block.IndexOf("SET totp_secret = @secret", StringComparison.Ordinal);
        overwrite.Should().BeGreaterThan(guard);
        // Ön kontrol ile UPDATE arasındaki yarışta da açık 2FA'nın sırrı ezilmemeli.
        block.Should().Contain("AND is_2fa_enabled = FALSE");
    }

    [Fact]
    public void BackupCodes_RegenerationRequiresSecondFactorOrFreshEnableGrant()
    {
        // Aksi halde oturum sahibi yeni yedek kodlar alıp çalınmış şifreyle TOTP'siz giriş yapabilir.
        var programText = ReadProgram();
        var block = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/2fa/backup-codes\"",
            "app.MapGet(\"/api/v1/auth/2fa/backup-codes/count\"");

        var check = block.IndexOf("AuthAccountPolicy.CheckBackupCodeIssue(", StringComparison.Ordinal);
        var delete = block.IndexOf("DELETE FROM totp_backup_codes WHERE user_id = @userId", StringComparison.Ordinal);

        check.Should().BeGreaterThanOrEqualTo(0);
        delete.Should().BeGreaterThan(check);
        block.Should().Contain("ReadOptionalJsonRequestAsync<TwoFactorCodeRequest>(");
        block.Should().Contain("$\"2fa:fresh-enable:{userId}\"");
        block.Should().Contain("TWO_FACTOR_NOT_ENABLED");

        var enableBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/auth/2fa/enable\"",
            "app.MapPost(\"/api/v1/auth/2fa/disable\"");
        enableBlock.Should().Contain("$\"2fa:fresh-enable:{userId}\"");
        enableBlock.IndexOf("$\"2fa:fresh-enable:{userId}\"", StringComparison.Ordinal)
            .Should().BeGreaterThan(enableBlock.IndexOf("TotpService.Validate(", StringComparison.Ordinal));
    }

    private static string ReadProgram() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
