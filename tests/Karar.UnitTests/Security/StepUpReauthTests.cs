using System.Security.Cryptography;
using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

// Regresyon: Google hesabı bağlama, 2FA kapatma / yeniden kurma ve yedek kod yenileme yalnızca
// bearer access token istiyordu. Çalınmış 15 dk'lık token kalıcı girişe (Google link) ve 2FA'nın
// ele geçirilmesine dönüşebiliyordu; Google girişi bağlı şifreli hesapta TOTP'yi atlıyordu.
public sealed class StepUpReauthTests
{
    private const string TotpSecret = "JBSWY3DPEHPK3PXP";
    private static readonly string PasswordHash = PasswordService.Hash("dogru-sifre-123");

    // ── Davranışsal: StepUpAuthPolicy ───────────────────────────────────────

    [Fact]
    public void PasswordAccount_BearerTokenAlone_IsNotEnough()
    {
        StepUpAuthPolicy.Evaluate(PasswordHash, is2faEnabled: true, TotpSecret, password: null, totpCode: null)
            .Should().Be(StepUpAuthPolicy.Result.PasswordRequired);
    }

    [Fact]
    public void PasswordAccount_RequiresCorrectPassword()
    {
        StepUpAuthPolicy.Evaluate(PasswordHash, false, null, "yanlis-sifre-123", null)
            .Should().Be(StepUpAuthPolicy.Result.InvalidPassword);
        StepUpAuthPolicy.Evaluate(PasswordHash, false, null, "dogru-sifre-123", null)
            .Should().Be(StepUpAuthPolicy.Result.Ok);
    }

    [Fact]
    public void PasswordAccount_TotpDoesNotReplacePassword()
    {
        StepUpAuthPolicy.Evaluate(PasswordHash, true, TotpSecret, null, CurrentTotp(TotpSecret))
            .Should().Be(StepUpAuthPolicy.Result.PasswordRequired);
    }

    [Fact]
    public void PasswordlessAccountWith2fa_RequiresValidTotp()
    {
        StepUpAuthPolicy.Evaluate(null, true, TotpSecret, null, null)
            .Should().Be(StepUpAuthPolicy.Result.TwoFactorRequired);
        StepUpAuthPolicy.Evaluate(null, true, TotpSecret, null, InvalidTotp(TotpSecret))
            .Should().Be(StepUpAuthPolicy.Result.InvalidTwoFactor);
        StepUpAuthPolicy.Evaluate(null, true, TotpSecret, null, CurrentTotp(TotpSecret))
            .Should().Be(StepUpAuthPolicy.Result.Ok);
    }

    [Fact]
    public void PasswordlessAccountWithout2fa_CannotStepUp()
    {
        StepUpAuthPolicy.Evaluate(null, false, null, "anything-123", "123456")
            .Should().Be(StepUpAuthPolicy.Result.Unavailable);
        // Secret var ama 2FA etkin değil (kurulum yarıda) → yine yeniden doğrulama yok.
        StepUpAuthPolicy.Evaluate(null, false, TotpSecret, null, CurrentTotp(TotpSecret))
            .Should().Be(StepUpAuthPolicy.Result.Unavailable);
    }

    // ── Kaynak sözleşmesi: endpoint bağlantıları (Postgres/Firebase gerektirir) ──

    [Fact]
    public void GoogleLink_RequiresStepUpBeforeLinking()
    {
        var block = Slice("app.MapPost(\"/api/v1/auth/google/link\"", "app.MapDelete(\"/api/v1/auth/google\"");

        block.Should().Contain("GoogleLinkRequest request");
        AssertBefore(block, "RequireStepUpAsync(connection, bruteForce, userId, request.Password, request.TotpCode)",
            "UPDATE users SET google_id = @googleId");
    }

    [Fact]
    public void TwoFactorDisable_RequiresStepUpAndDeletesBackupCodes()
    {
        // Single kept implementation (sec/10 AuthAccountPolicy.CheckTwoFactorDisable): the password AND,
        // with 2FA on, a TOTP or backup code - stricter than password-only step-up - with the per-account
        // "stepup" brute-force counter from sec/09.
        var block = Slice("app.MapPost(\"/api/v1/auth/2fa/disable\"", "app.MapGet(\"/api/v1/users/me/sessions\"");

        AssertBefore(block, "BruteForceService.AccountIdentityFor(\"stepup\", userId)", "bruteForce.IsLockedOutAsync(bfIdentity)");
        AssertBefore(block, "bruteForce.IsLockedOutAsync(bfIdentity)", "AuthAccountPolicy.CheckTwoFactorDisable(");
        AssertBefore(block, "AuthAccountPolicy.CheckTwoFactorDisable(", "UPDATE users SET is_2fa_enabled = FALSE");
        block.Should().Contain("bruteForce.RecordFailedAttemptAsync(bfIdentity)",
            "wrong password / TOTP / backup-code guesses must count against the account");
        block.Should().NotContain("RequireStepUpAsync(",
            "2FA disable is gated once, by the stricter password + second-factor check");
        block.Should().Contain("DELETE FROM totp_backup_codes WHERE user_id = @id");
    }

    [Fact]
    public void TwoFactorSetup_CannotReplaceSecretWhileEnabled()
    {
        var block = Slice("app.MapPost(\"/api/v1/auth/2fa/setup\"", "app.MapPost(\"/api/v1/auth/2fa/enable\"");

        block.Should().Contain("UPDATE users SET totp_secret = @secret, updated_at = NOW() WHERE id = @id AND deleted_at IS NULL AND is_2fa_enabled = FALSE");
        block.Should().Contain("\"TWO_FACTOR_ALREADY_ENABLED\"");
    }

    [Fact]
    public void TwoFactorEnable_CannotMintBackupCodeGrantWhenAlreadyEnabled()
    {
        // Regression (sec/09 lockout + sec/10 grant): with 2FA already on, /2fa/enable validated TOTP
        // guesses with no per-account lockout and, on a hit, set the one-shot "fresh-enable" grant that
        // /2fa/backup-codes accepts instead of a code - bypassing that endpoint's "stepup" lockout.
        var block = Slice("app.MapPost(\"/api/v1/auth/2fa/enable\"", "app.MapPost(\"/api/v1/auth/2fa/disable\"");

        block.Should().Contain("SELECT totp_secret, is_2fa_enabled FROM users");
        AssertBefore(block, "if (alreadyEnabled)", "TotpService.Validate(secret, request.Code)");
        AssertBefore(block, "\"TWO_FACTOR_ALREADY_ENABLED\"", "TotpService.Validate(secret, request.Code)");
        block.Should().Contain("WHERE id = @id AND is_2fa_enabled = FALSE",
            "only the request that actually turns 2FA on may receive the grant");
        AssertBefore(block, "if (await update.ExecuteNonQueryAsync() != 1)", "2fa:fresh-enable:");
    }

    [Fact]
    public void BackupCodes_RequireEnabled2faAndStepUpToReplaceExistingCodes()
    {
        // Single kept implementation (sec/10 AuthAccountPolicy.CheckBackupCodeIssue): every issuance -
        // the first one included - needs a valid TOTP code or the one-shot grant set by 2fa/enable, which
        // is stricter than sec/09's password step-up for replacements only. Failed TOTP guesses use the
        // per-account "stepup" counter from sec/09.
        var block = Slice("app.MapPost(\"/api/v1/auth/2fa/backup-codes\"", "app.MapGet(\"/api/v1/auth/2fa/backup-codes/count\"");

        block.Should().Contain("\"TWO_FACTOR_NOT_ENABLED\"");
        AssertBefore(block, "bruteForce.IsLockedOutAsync(bfIdentity)", "AuthAccountPolicy.CheckBackupCodeIssue(");
        AssertBefore(block, "AuthAccountPolicy.CheckBackupCodeIssue(", "DELETE FROM totp_backup_codes WHERE user_id = @userId");
        block.Should().Contain("bruteForce.RecordFailedAttemptAsync(bfIdentity)");
        block.Should().NotContain("RequireStepUpAsync(",
            "backup-code issuance is gated once, by the second-factor check");
    }

    [Fact]
    public void GoogleSignIn_EnforcesTotpForLinkedPasswordAccounts()
    {
        var block = Slice("app.MapPost(\"/api/v1/auth/google\",", "app.MapPost(\"/api/v1/auth/google/link\"");

        block.Should().Contain("is_2fa_enabled, totp_secret");
        block.Should().Contain("if (authProvider == \"password\" && is2faEnabled)");
        AssertBefore(block, "return BadRequest(\"TWO_FACTOR_REQUIRED\"", "jwtService.GenerateAccessToken(");
    }

    [Fact]
    public void StepUpHelper_UsesPolicyAndPerAccountBruteForceCounter()
    {
        var text = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var start = text.IndexOf("static async Task<IResult?> RequireStepUpAsync(", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0);
        var helper = text[start..Math.Min(text.Length, start + 3000)];

        helper.Should().Contain("BruteForceService.AccountIdentityFor(\"stepup\", userId)");
        helper.Should().Contain("bruteForce.IsLockedOutAsync(bfIdentity)");
        helper.Should().Contain("StepUpAuthPolicy.Evaluate(");
        helper.Should().Contain("bruteForce.RecordFailedAttemptAsync(bfIdentity)");
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────

    private static string Slice(string startMarker, string endMarker)
    {
        var text = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }

    private static void AssertBefore(string block, string first, string second)
    {
        var a = block.IndexOf(first, StringComparison.Ordinal);
        var b = block.IndexOf(second, StringComparison.Ordinal);
        a.Should().BeGreaterThan(0, $"'{first}' should be present");
        b.Should().BeGreaterThan(0, $"'{second}' should be present");
        a.Should().BeLessThan(b, $"'{first}' must run before '{second}'");
    }

    // RFC 6238 (SHA-1, 30 sn, 6 hane) — TotpService ile aynı parametreler.
    private static string CurrentTotp(string base32Secret) =>
        Totp(Base32Decode(base32Secret), DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30).ToString("D6");

    private static string InvalidTotp(string base32Secret)
    {
        var key = Base32Decode(base32Secret);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var valid = new HashSet<int>();
        for (var i = -2; i <= 2; i++) valid.Add(Totp(key, counter + i));
        var candidate = 0;
        while (valid.Contains(candidate)) candidate++;
        return candidate.ToString("D6");
    }

    private static int Totp(byte[] key, long counter)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        var hmac = HMACSHA1.HashData(key, counterBytes);
        var offset = hmac[^1] & 0x0F;
        var binary = ((hmac[offset] & 0x7F) << 24) | (hmac[offset + 1] << 16) | (hmac[offset + 2] << 8) | hmac[offset + 3];
        return binary % 1_000_000;
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var bitCount = 0;
        var result = new List<byte>();
        foreach (var c in input.TrimEnd('='))
        {
            bits = (bits << 5) | alphabet.IndexOf(c);
            bitCount += 5;
            if (bitCount >= 8)
            {
                result.Add((byte)(bits >> (bitCount - 8)));
                bitCount -= 8;
            }
        }
        return [.. result];
    }
}
