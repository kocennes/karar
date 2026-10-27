namespace Karar.Api.Services;

public enum TwoFactorDisableCheck
{
    Allowed,
    PasswordRequired,
    InvalidPassword,
    SecondFactorRequired,
    InvalidTotpCode,
    VerifyBackupCode,
}

public enum BackupCodeIssueCheck
{
    Allowed,
    NotEnabled,
    SecondFactorRequired,
    InvalidTotpCode,
}

// Hesap güvenliği kararları — Program.cs'teki uç noktalar DB'den okuduğu durumu buraya verir.
// Saf fonksiyonlar: Postgres/Redis olmadan birim test edilebilir.
public static class AuthAccountPolicy
{
    // 2FA'yı kapatmak için oturum (JWT) tek başına yetmez: şifreli hesapta şifre,
    // 2FA açıksa ayrıca geçerli TOTP kodu veya yedek kod gerekir.
    public static TwoFactorDisableCheck CheckTwoFactorDisable(
        string? passwordHash,
        bool is2faEnabled,
        string? totpSecret,
        string? password,
        string? totpCode,
        string? backupCode)
    {
        if (passwordHash is not null)
        {
            if (string.IsNullOrWhiteSpace(password))
                return TwoFactorDisableCheck.PasswordRequired;
            if (!PasswordService.Verify(password, passwordHash))
                return TwoFactorDisableCheck.InvalidPassword;
        }

        if (!is2faEnabled)
            return TwoFactorDisableCheck.Allowed;

        if (!string.IsNullOrWhiteSpace(totpCode)
            && totpSecret is not null
            && TotpService.Validate(totpSecret, totpCode))
            return TwoFactorDisableCheck.Allowed;

        if (!string.IsNullOrWhiteSpace(backupCode))
            return TwoFactorDisableCheck.VerifyBackupCode;

        return string.IsNullOrWhiteSpace(totpCode)
            ? TwoFactorDisableCheck.SecondFactorRequired
            : TwoFactorDisableCheck.InvalidTotpCode;
    }

    // Yeni yedek kodlar, TOTP'siz girişe izin verdiği için yalnızca 2FA'yı az önce (geçerli kodla)
    // açan oturuma ya da geçerli TOTP kodu sunana verilir.
    public static BackupCodeIssueCheck CheckBackupCodeIssue(
        bool is2faEnabled,
        string? totpSecret,
        string? totpCode,
        bool hasFreshEnableGrant)
    {
        if (!is2faEnabled)
            return BackupCodeIssueCheck.NotEnabled;

        if (!string.IsNullOrWhiteSpace(totpCode))
            return totpSecret is not null && TotpService.Validate(totpSecret, totpCode)
                ? BackupCodeIssueCheck.Allowed
                : BackupCodeIssueCheck.InvalidTotpCode;

        return hasFreshEnableGrant
            ? BackupCodeIssueCheck.Allowed
            : BackupCodeIssueCheck.SecondFactorRequired;
    }
}
