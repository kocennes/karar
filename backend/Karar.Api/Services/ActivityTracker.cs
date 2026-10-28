namespace Karar.Api.Services;

/// <summary>
/// Ücretsiz katman boşta kalma kapısı (Render Free + Neon Free + Upstash Free).
/// Kullanıcı isteği gelmediği sürece zamanlayıcı işçileri Postgres'e dokunmaz; böylece Neon'un
/// 5 dakikalık otomatik askıya alması bozulmaz ve aylık compute bütçesi boşa harcanmaz.
/// Bir istek geldiğinde boşta durumu anında biter; bir sonraki tick normal işler.
/// </summary>
public sealed class ActivityTracker
{
    public const string EnabledKey = "FreeTier:Enabled";
    public const string IdleAfterMinutesKey = "FreeTier:IdleAfterMinutes";
    public const int DefaultIdleAfterMinutes = 10;

    private readonly TimeProvider _time;
    private long _lastUserRequestTicks;
    private long _lastPendingWorkTicks;

    // DI: FreeTier:Enabled verilmemişse yalnızca Production'da açık.
    public ActivityTracker(IConfiguration configuration, IHostEnvironment environment)
        : this(
            configuration.GetValue<bool?>(EnabledKey) ?? environment.IsProduction(),
            TimeSpan.FromMinutes(ResolveIdleMinutes(configuration)),
            TimeProvider.System)
    {
    }

    public ActivityTracker(bool enabled, TimeSpan idleAfter, TimeProvider timeProvider)
    {
        Enabled = enabled;
        IdleAfter = idleAfter;
        _time = timeProvider;

        // Başlangıç bir etkinlik sayılır: deploy/soğuk başlangıç sonrası bekleyen işler
        // ilk IdleAfter süresi içinde boşaltılır, sonra kapı kapanır.
        var now = UtcNowTicks;
        _lastUserRequestTicks = now;
        _lastPendingWorkTicks = now;
    }

    public bool Enabled { get; }

    public TimeSpan IdleAfter { get; }

    /// Son gerçek kullanıcı isteği (sağlık ping'leri ve crawler istekleri hariç).
    public DateTime LastUserRequestUtc =>
        new(Volatile.Read(ref _lastUserRequestTicks), DateTimeKind.Utc);

    /// Son etkinlik: kullanıcı isteği ya da bir işçinin başka bir işçiye bıraktığı iş.
    public DateTime LastActivityUtc =>
        new(Math.Max(Volatile.Read(ref _lastUserRequestTicks), Volatile.Read(ref _lastPendingWorkTicks)),
            DateTimeKind.Utc);

    public void MarkRequest() => Volatile.Write(ref _lastUserRequestTicks, UtcNowTicks);

    /// Bir işçi başka bir işçinin işleyeceği DB işi üretti (ör. outbox'a bildirim yazdı);
    /// API bir IdleAfter süresi daha aktif sayılır ki iş kuyrukta beklemesin.
    public void MarkPendingWork() => Volatile.Write(ref _lastPendingWorkTicks, UtcNowTicks);

    public bool IsIdle() => IsIdle(IdleAfter);

    public bool IsIdle(TimeSpan threshold) =>
        Enabled && _time.GetUtcNow().UtcDateTime - LastActivityUtc >= threshold;

    private long UtcNowTicks => _time.GetUtcNow().UtcTicks;

    private static int ResolveIdleMinutes(IConfiguration configuration)
    {
        var minutes = configuration.GetValue<int?>(IdleAfterMinutesKey) ?? DefaultIdleAfterMinutes;
        return minutes > 0 ? minutes : DefaultIdleAfterMinutes;
    }
}
