namespace Karar.Api.Services;

// Hassas hesap işlemleri (Google hesabı bağlama, 2FA kapatma, yedek kod yenileme) için
// bearer access token tek başına yetmez; hesabın sahip olduğu bir sırla yeniden doğrulama gerekir:
//  - şifreli hesap            → mevcut şifre
//  - şifresiz hesap + 2FA açık → geçerli TOTP kodu
//  - ikisi de yok              → yeniden doğrulama yapılamaz, işlem reddedilir
public static class StepUpAuthPolicy
{
    public enum Result
    {
        Ok,
        PasswordRequired,
        InvalidPassword,
        TwoFactorRequired,
        InvalidTwoFactor,
        Unavailable,
    }

    public static Result Evaluate(
        string? passwordHash,
        bool is2faEnabled,
        string? totpSecret,
        string? password,
        string? totpCode)
    {
        if (!string.IsNullOrEmpty(passwordHash))
        {
            if (string.IsNullOrEmpty(password)) return Result.PasswordRequired;
            return PasswordService.Verify(password, passwordHash) ? Result.Ok : Result.InvalidPassword;
        }

        if (is2faEnabled && !string.IsNullOrEmpty(totpSecret))
        {
            if (string.IsNullOrWhiteSpace(totpCode)) return Result.TwoFactorRequired;
            return TotpService.Validate(totpSecret, totpCode) ? Result.Ok : Result.InvalidTwoFactor;
        }

        return Result.Unavailable;
    }
}
