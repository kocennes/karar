using System.Security.Cryptography;
using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Security;

public sealed class AuthAccountPolicyTests
{
    private const string Secret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";
    private static readonly string PasswordHash = PasswordService.Hash("dogru-sifre-123");

    // ── 2FA kapatma ─────────────────────────────────────────────────────────

    [Fact]
    public void Disable_WithSessionOnly_IsRejected()
    {
        // Eski istemci davranışı: gövdesiz POST (yalnızca JWT) → 2FA kapanmamalı.
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, null, null, null)
            .Should().Be(TwoFactorDisableCheck.PasswordRequired);
    }

    [Fact]
    public void Disable_WithAnyNonEmptyPassword_IsRejected()
    {
        // Diyalog yalnızca alanın boş olmadığını kontrol ediyordu; herhangi bir karakter yetmemeli.
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, "x", CurrentCode(Secret), null)
            .Should().Be(TwoFactorDisableCheck.InvalidPassword);
    }

    [Fact]
    public void Disable_WithStolenPasswordButNoSecondFactor_IsRejected()
    {
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, "dogru-sifre-123", null, null)
            .Should().Be(TwoFactorDisableCheck.SecondFactorRequired);
    }

    [Fact]
    public void Disable_WithWrongTotpCode_IsRejected()
    {
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, "dogru-sifre-123", WrongCode(Secret), null)
            .Should().Be(TwoFactorDisableCheck.InvalidTotpCode);
    }

    [Fact]
    public void Disable_WithPasswordAndValidTotp_IsAllowed()
    {
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, "dogru-sifre-123", CurrentCode(Secret), null)
            .Should().Be(TwoFactorDisableCheck.Allowed);
    }

    [Fact]
    public void Disable_WithPasswordAndBackupCode_RequiresBackupCodeLookup()
    {
        // Authenticator'ı kaybeden kullanıcı yedek kodla kapatabilmeli (kod DB'de tüketilerek doğrulanır).
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, "dogru-sifre-123", null, "ABCD-EFGH")
            .Should().Be(TwoFactorDisableCheck.VerifyBackupCode);
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, "dogru-sifre-123", WrongCode(Secret), "ABCD-EFGH")
            .Should().Be(TwoFactorDisableCheck.VerifyBackupCode);
    }

    [Fact]
    public void Disable_BackupCodeNeverSkipsPasswordCheck()
    {
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, Secret, "yanlis", null, "ABCD-EFGH")
            .Should().Be(TwoFactorDisableCheck.InvalidPassword);
    }

    [Fact]
    public void Disable_GoogleAccountWithoutPassword_RequiresSecondFactor()
    {
        AuthAccountPolicy.CheckTwoFactorDisable(null, true, Secret, null, null, null)
            .Should().Be(TwoFactorDisableCheck.SecondFactorRequired);
        AuthAccountPolicy.CheckTwoFactorDisable(null, true, Secret, null, CurrentCode(Secret), null)
            .Should().Be(TwoFactorDisableCheck.Allowed);
    }

    [Fact]
    public void Disable_WhenNotEnabled_StillRequiresPasswordButNoSecondFactor()
    {
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, false, Secret, null, null, null)
            .Should().Be(TwoFactorDisableCheck.PasswordRequired);
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, false, Secret, "dogru-sifre-123", null, null)
            .Should().Be(TwoFactorDisableCheck.Allowed);
    }

    [Fact]
    public void Disable_WithMissingTotpSecret_DoesNotAcceptAnyCode()
    {
        AuthAccountPolicy.CheckTwoFactorDisable(PasswordHash, true, null, "dogru-sifre-123", "123456", null)
            .Should().Be(TwoFactorDisableCheck.InvalidTotpCode);
    }

    // ── Yedek kod üretimi ───────────────────────────────────────────────────

    [Fact]
    public void BackupCodes_WithSessionOnly_AreNotIssued()
    {
        AuthAccountPolicy.CheckBackupCodeIssue(true, Secret, null, hasFreshEnableGrant: false)
            .Should().Be(BackupCodeIssueCheck.SecondFactorRequired);
    }

    [Fact]
    public void BackupCodes_AfterFreshEnable_AreIssuedWithoutCode()
    {
        // Mevcut istemci akışı: enable2fa(code) → generateBackupCodes() (gövdesiz).
        AuthAccountPolicy.CheckBackupCodeIssue(true, Secret, null, hasFreshEnableGrant: true)
            .Should().Be(BackupCodeIssueCheck.Allowed);
    }

    [Fact]
    public void BackupCodes_WithValidOrInvalidTotp()
    {
        AuthAccountPolicy.CheckBackupCodeIssue(true, Secret, CurrentCode(Secret), hasFreshEnableGrant: false)
            .Should().Be(BackupCodeIssueCheck.Allowed);
        AuthAccountPolicy.CheckBackupCodeIssue(true, Secret, WrongCode(Secret), hasFreshEnableGrant: true)
            .Should().Be(BackupCodeIssueCheck.InvalidTotpCode);
    }

    [Fact]
    public void BackupCodes_WhenTwoFactorDisabled_AreNotIssued()
    {
        AuthAccountPolicy.CheckBackupCodeIssue(false, Secret, CurrentCode(Secret), hasFreshEnableGrant: true)
            .Should().Be(BackupCodeIssueCheck.NotEnabled);
    }

    // ── Google girişi / bekleyen kayıt ──────────────────────────────────────

    [Fact]
    public void GoogleCollision_PendingPasswordRegistration_IsTakenOverByVerifiedGoogleEmail()
    {
        AuthAccountPolicy.ResolveGoogleEmailCollision(
                existingEmailVerified: false, existingAuthProvider: "password",
                existingIsDeleted: false, firebaseEmailVerified: true)
            .Should().Be(GoogleEmailCollision.TakeOverPendingRegistration);
    }

    [Theory]
    [InlineData(true, "password", false, true)]   // doğrulanmış şifreli hesap: sessizce birleştirilmez
    [InlineData(false, "password", false, false)] // Firebase e-postayı doğrulamamış
    [InlineData(false, "password", true, true)]   // silinmiş hesap (kurtarma süresi)
    [InlineData(false, "google", false, true)]    // başka bir Google kimliği
    public void GoogleCollision_OtherRows_AreConflicts(bool verified, string provider, bool deleted, bool firebaseVerified)
    {
        AuthAccountPolicy.ResolveGoogleEmailCollision(verified, provider, deleted, firebaseVerified)
            .Should().Be(GoogleEmailCollision.Conflict);
    }

    [Fact]
    public void FirebaseEmailVerifiedClaim_IsParsedStrictly()
    {
        AuthAccountPolicy.IsFirebaseEmailVerified(new Dictionary<string, object> { ["email_verified"] = true })
            .Should().BeTrue();
        AuthAccountPolicy.IsFirebaseEmailVerified(new Dictionary<string, object> { ["email_verified"] = "true" })
            .Should().BeTrue();
        AuthAccountPolicy.IsFirebaseEmailVerified(new Dictionary<string, object> { ["email_verified"] = false })
            .Should().BeFalse();
        AuthAccountPolicy.IsFirebaseEmailVerified(new Dictionary<string, object> { ["email"] = "a@b.c" })
            .Should().BeFalse();
    }

    [Fact]
    public void RegistrantPassword_IsKeptOnlyOnRegisteringDevice()
    {
        var registering = Guid.NewGuid();

        AuthAccountPolicy.KeepsRegistrantPassword(registering, registering).Should().BeTrue();
        // Saldırgan A cihazından kurbanın e-postasıyla kayıt açtı, kurban kendi cihazında kodu girdi.
        AuthAccountPolicy.KeepsRegistrantPassword(registering, Guid.NewGuid()).Should().BeFalse();
        AuthAccountPolicy.KeepsRegistrantPassword(registering, null).Should().BeFalse();
    }

    // RFC 6238 (SHA-1, 30 sn, 6 hane) — TotpService ile aynı parametreler.
    private static string CurrentCode(string base32Secret) =>
        Generate(base32Secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30).ToString("D6");

    // ±1 adım toleransının dışında kalan, geçerli olamayacak bir kod.
    private static string WrongCode(string base32Secret)
    {
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var valid = Enumerable.Range(-2, 5).Select(i => Generate(base32Secret, counter + i)).ToHashSet();
        var candidate = 0;
        while (valid.Contains(candidate)) candidate++;
        return candidate.ToString("D6");
    }

    private static int Generate(string base32Secret, long counter)
    {
        var key = Base32Decode(base32Secret);
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        var hmac = HMACSHA1.HashData(key, counterBytes);
        var offset = hmac[^1] & 0x0F;
        var binary = ((hmac[offset] & 0x7F) << 24)
            | (hmac[offset + 1] << 16)
            | (hmac[offset + 2] << 8)
            | hmac[offset + 3];
        return binary % 1_000_000;
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var bitCount = 0;
        var result = new List<byte>();
        foreach (var c in input)
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
