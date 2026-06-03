using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

public sealed class ReportRateLimitTests
{
    // In-memory sliding window rate limiter — mirrors RedisService.IsAllowedAsync Lua logic.
    private static Func<string, string, int, TimeSpan, Task<bool>> InMemoryLimiter()
    {
        var windows = new Dictionary<string, List<long>>();
        return (endpoint, identity, limit, window) =>
        {
            var key = $"{endpoint}:{identity}";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowMs = (long)window.TotalMilliseconds;

            if (!windows.TryGetValue(key, out var timestamps))
                windows[key] = timestamps = new List<long>();

            timestamps.RemoveAll(ts => ts < now - windowMs);

            if (timestamps.Count < limit)
            {
                timestamps.Add(now);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        };
    }

    // ── Cihaz başına 10 rapor kabul ─────────────────────────────────────────

    [Fact]
    public async Task AllowsTenReportsFromSameDevice()
    {
        var svc = new ReportAbuseProtectionService(InMemoryLimiter());
        var device = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
        {
            var (allowed, _) = await svc.CheckDeviceRateLimitAsync(device);
            allowed.Should().BeTrue($"request {i + 1} of 10 must be allowed");
        }
    }

    // ── 11. rapor reddedilmeli; Retry-After = 1 saat ────────────────────────

    [Fact]
    public async Task RejectsEleventhReport_Returns429WithRetryAfter3600()
    {
        var svc = new ReportAbuseProtectionService(InMemoryLimiter());
        var device = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            await svc.CheckDeviceRateLimitAsync(device);

        var (allowed, retryAfterSeconds) = await svc.CheckDeviceRateLimitAsync(device);

        allowed.Should().BeFalse("11th request exceeds hourly limit of 10");
        retryAfterSeconds.Should().Be(3600, "Retry-After must be one full hour (3600 s)");
    }

    // ── Farklı cihazın sayacı bağımsız olmalı ────────────────────────────────

    [Fact]
    public async Task DifferentDevicesHaveIndependentCounters()
    {
        var svc = new ReportAbuseProtectionService(InMemoryLimiter());
        var deviceA = Guid.NewGuid();
        var deviceB = Guid.NewGuid();

        for (var i = 0; i < 10; i++)
            await svc.CheckDeviceRateLimitAsync(deviceA);

        var (allowed, _) = await svc.CheckDeviceRateLimitAsync(deviceB);
        allowed.Should().BeTrue("device B has its own counter, unaffected by device A's limit");
    }

    // ── Redis erişilemez → fail-open: istek kabul edilmeli ──────────────────

    [Fact]
    public async Task RedisUnavailable_FailsOpenAndAllowsRequest()
    {
        // RedisService.IsAllowedAsync catches all Redis exceptions and returns true (fail-open).
        // We model that contracted behavior: the delegate returns true when the backing store fails.
        Func<string, string, int, TimeSpan, Task<bool>> failOpen =
            (_, _, _, _) => Task.FromResult(true);

        var svc = new ReportAbuseProtectionService(failOpen);
        var device = Guid.NewGuid();

        var (allowed, _) = await svc.CheckDeviceRateLimitAsync(device);
        allowed.Should().BeTrue("Redis downtime must not block legitimate report submissions");
    }

    // ── Contract: RedisService.IsAllowedAsync fail-open kaynak kodu ─────────

    [Fact]
    public void RedisService_IsAllowedAsync_FailsOpen_OnRedisException()
    {
        var redisServiceText = TestRepoPaths.ReadText(
            "backend", "Karar.Api", "Services", "RedisService.cs");

        redisServiceText.Should().Contain("return true; // Hata durumunda isteğe izin ver (fail-open)",
            because: "IsAllowedAsync must fail open so a Redis outage never blocks report submissions");
    }

    // ── Contract: endpoint 429 response stable error code + Retry-After ─────

    [Fact]
    public void ReportEndpoint_Returns429WithStableErrorCode_OnRateLimit()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Should().Contain("RATE_LIMIT_REPORTS",
            because: "clients depend on this stable error code to distinguish rate limiting from other errors");
        program.Should().Contain("Retry-After",
            because: "RFC 6585 requires Retry-After header on 429 responses");
        program.Should().Contain("CheckDeviceRateLimitAsync",
            because: "the report endpoint must invoke the Redis-backed sliding window check");
    }
}
