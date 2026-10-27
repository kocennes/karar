namespace Karar.Api.Services;

// /auth/refresh: sunulan refresh token'ın durumuna göre karar.
//  - aktif                                   → Rotate (eski iptal, aynı aileye yeni token)
//  - rotasyonla iptal, pencere içinde, aile açık → ConcurrentGrace (istemci refresh'i single-flight
//    değil; aynı token'la eşzamanlı yenilemeler kullanıcıyı düşürmesin)
//  - rotasyonla iptal, pencere dışında, aile açık → ReuseDetected (token sızmış: tüm aile iptal)
//  - logout / şifre değişikliği / ban ile iptal veya aile zaten kapalı → Reject
public static class RefreshTokenRotationPolicy
{
    public static readonly TimeSpan ConcurrentRefreshGrace = TimeSpan.FromSeconds(15);

    public enum Decision
    {
        Rotate,
        ConcurrentGrace,
        ReuseDetected,
        Reject,
    }

    public static Decision Classify(
        DateTimeOffset? revokedAt,
        DateTimeOffset? rotatedAt,
        bool familyHasActiveToken,
        DateTimeOffset now)
    {
        if (revokedAt is null) return Decision.Rotate;
        if (rotatedAt is null || !familyHasActiveToken) return Decision.Reject;
        return now - rotatedAt.Value <= ConcurrentRefreshGrace
            ? Decision.ConcurrentGrace
            : Decision.ReuseDetected;
    }
}
