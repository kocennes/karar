using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;
using StackExchange.Redis;

namespace Karar.UnitTests.Security;

// Regresyon: login throttling yalnızca saldırganın kontrol ettiği (IP, cihaz) anahtarına bağlıydı.
// Cihaz token'ı serbestçe üretilebildiği için tek hesaba sınırsız şifre tahmini yapılabiliyordu;
// 2FA yolunda sayaç yedek kod doğrulanmadan temizleniyordu; Redis sliding-window üyesi
// milisaniye damgası olduğu için aynı ms'deki istekler tek kayıt sayılıyordu.
public sealed class LoginThrottleTests
{
    // ── Davranışsal: BruteForceService (in-memory IDatabase ile) ────────────

    [Fact]
    public async Task DeviceRotation_AloneNeverLocks_ButAccountIdentityDoes()
    {
        var (bruteForce, _) = CreateService();
        var victim = Guid.NewGuid();
        var account = BruteForceService.AccountIdentityFor("login", victim);

        // Saldırgan her denemede yeni bir cihaz token'ı kullanır (aynı IP).
        for (var attempt = 1; attempt <= BruteForceService.MaxAccountAttempts; attempt++)
        {
            var perDevice = BruteForceService.IdentityFor("203.0.113.10", "login", Guid.NewGuid());
            (await bruteForce.IsLockedOutAsync(perDevice)).Should().BeFalse();
            await bruteForce.RecordFailedAttemptAsync(perDevice);
            (await bruteForce.IsLockedOutAsync(perDevice)).Should().BeFalse(
                "a fresh device identity never reaches the IP+device threshold");

            var (locked, _) = await bruteForce.RecordFailedAttemptAsync(account, BruteForceService.MaxAccountAttempts);
            locked.Should().Be(attempt == BruteForceService.MaxAccountAttempts);
        }

        (await bruteForce.IsLockedOutAsync(account)).Should().BeTrue(
            "the per-account counter must lock regardless of IP/device rotation");
    }

    [Fact]
    public async Task AccountThreshold_IsHigherThanDeviceThreshold()
    {
        var (bruteForce, _) = CreateService();
        var account = BruteForceService.AccountIdentityFor("login", Guid.NewGuid());
        var device = BruteForceService.IdentityFor("203.0.113.10", "login", Guid.NewGuid());

        for (var i = 0; i < 10; i++)
        {
            await bruteForce.RecordFailedAttemptAsync(device);
            await bruteForce.RecordFailedAttemptAsync(account, BruteForceService.MaxAccountAttempts);
        }

        (await bruteForce.IsLockedOutAsync(device)).Should().BeTrue("default IP+device threshold stays 10");
        (await bruteForce.IsLockedOutAsync(account)).Should().BeFalse("account threshold is 20 to limit victim lock-out");
    }

    [Fact]
    public void AccountIdentityFor_Identifier_IsCaseInsensitiveHashedAndDeviceIndependent()
    {
        var a = BruteForceService.AccountIdentityFor("login", "Victim@Example.com");
        var b = BruteForceService.AccountIdentityFor("login", "  victim@example.com ");

        a.Should().Be(b);
        a.Should().NotContain("victim", "raw identifiers must not be written to Redis keys");
        a.Should().EndWith(":login");
        BruteForceService.AccountIdentityFor("login", "other@example.com").Should().NotBe(a);
    }

    [Fact]
    public void AccountIdentityFor_UserId_IsStable()
    {
        var userId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        BruteForceService.AccountIdentityFor("login", userId)
            .Should().Be("account:11111111222233334444555555555555:login");
    }

    // ── Kaynak sözleşmesi: /auth/login sıralaması (Postgres gerektirir) ─────

    [Fact]
    public void Login_ChecksPerAccountLockoutBeforeVerifyingPassword()
    {
        var block = LoginBlock();
        var accountLock = block.IndexOf("bruteForce.IsLockedOutAsync(accountBfIdentity)", StringComparison.Ordinal);
        var verify = block.IndexOf("PasswordService.Verify(request.Password", StringComparison.Ordinal);

        block.Should().Contain("BruteForceService.AccountIdentityFor(\"login\", userId)");
        block.Should().Contain("BruteForceService.AccountIdentityFor(\"login\", identifier)");
        accountLock.Should().BeGreaterThan(0);
        accountLock.Should().BeLessThan(verify);
        block.Should().Contain(
            "bruteForce.RecordFailedAttemptAsync(accountBfIdentity, BruteForceService.MaxAccountAttempts)");
    }

    [Fact]
    public void Login_ClearsCountersOnlyAfterEveryFactorSucceeded()
    {
        var block = LoginBlock();
        var clear = block.IndexOf("bruteForce.ClearAsync(bfIdentity)", StringComparison.Ordinal);
        var backupFailure = block.IndexOf("\"INVALID_BACKUP_CODE\"", StringComparison.Ordinal);
        var twoFactorFailure = block.IndexOf("\"TWO_FACTOR_REQUIRED\"", StringComparison.Ordinal);

        clear.Should().BeGreaterThan(0);
        clear.Should().BeGreaterThan(backupFailure,
            "clearing before the backup code is checked lets 2FA guesses escape the lockout");
        clear.Should().BeGreaterThan(twoFactorFailure);
        block.Should().Contain("bruteForce.ClearAsync(accountBfIdentity)");
    }

    [Fact]
    public void RedisSlidingWindow_UsesUniqueSortedSetMember()
    {
        var redis = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "RedisService.cs");

        redis.Should().NotContain("redis.call('ZADD', key, now, now)",
            "requests in the same millisecond collapse into one member and undercount");
        redis.Should().Contain("redis.call('ZADD', key, now, member)");
        redis.Should().Contain("$\"{now}-{Guid.NewGuid():N}\"");
    }

    private static string LoginBlock()
    {
        var text = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        const string startMarker = "app.MapPost(\"/api/v1/auth/login\"";
        const string endMarker = "app.MapPost(\"/api/v1/auth/refresh\"";
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        return text[start..end];
    }

    private static (BruteForceService Service, InMemoryRedisDatabase Store) CreateService()
    {
        // BruteForceService canlı Redis bağlantısı (RedisService) ister; test için yalnızca
        // _db alanını in-memory bir IDatabase ile dolduruyoruz.
        // Ortak in-memory IDatabase (InMemoryRedisDatabase.cs; SubnetBanServiceTests ile paylaşılır).
        var (database, store) = InMemoryRedisDatabase.Create();
        var service = (BruteForceService)RuntimeHelpers.GetUninitializedObject(typeof(BruteForceService));
        typeof(BruteForceService)
            .GetField("_db", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(service, database);
        return (service, store);
    }
}
