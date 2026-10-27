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

public enum GoogleEmailCollision
{
    TakeOverPendingRegistration,
    Conflict,
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

    // Google girişi, e-postayı tutan başka bir satırla karşılaştığında: yalnızca doğrulanmamış
    // (sahibi kanıtlanmamış) şifreli bir kayıt, e-postası Google tarafından doğrulanmış kişiye devredilir.
    public static GoogleEmailCollision ResolveGoogleEmailCollision(
        bool existingEmailVerified,
        string existingAuthProvider,
        bool existingIsDeleted,
        bool firebaseEmailVerified)
    {
        return !existingEmailVerified
            && !existingIsDeleted
            && existingAuthProvider == "password"
            && firebaseEmailVerified
                ? GoogleEmailCollision.TakeOverPendingRegistration
                : GoogleEmailCollision.Conflict;
    }

    public static bool IsFirebaseEmailVerified(IReadOnlyDictionary<string, object> claims)
    {
        if (!claims.TryGetValue("email_verified", out var value) || value is null)
            return false;

        return value switch
        {
            bool b => b,
            string s => bool.TryParse(s, out var parsed) && parsed,
            _ => bool.TryParse(value.ToString(), out var parsed) && parsed,
        };
    }

    // E-posta doğrulaması e-postanın sahibini kanıtlar, şifreyi kimin koyduğunu değil.
    // Kodu kaydı başlatan cihazdan farklı bir cihaz girerse kayıt şifresi güvenilmezdir.
    public static bool KeepsRegistrantPassword(Guid registeredDeviceId, Guid? verifyingDeviceId) =>
        verifyingDeviceId is { } deviceId && deviceId == registeredDeviceId;
}
