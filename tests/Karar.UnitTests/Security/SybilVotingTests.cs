using System.Net;
using FluentAssertions;
using Karar.Api.Common;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

/// <summary>
/// Regression (sybil voting): device registration was limited only per /24 (IPv4) or per /64 (IPv6), so a
/// single subscriber holding a /56 (256 × /64) or /48 (65,536 × /64) could mint devices without bound and
/// cast one counted vote per device. Banning those devices afterwards did not remove their votes from the
/// public Haklı/Haksız counters.
/// </summary>
public sealed class SybilVotingTests
{
    private static string ProgramText => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void DeviceRegister_AlsoRateLimitsWiderIpv6Prefixes()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/devices/register\", async (",
            "app.MapPut(\"/api/v1/devices/fcm-token\", async (");

        // Single implementation shared with SubnetBanServiceTests (per-IP budget first, then network buckets).
        block.Should().Contain("DeviceRegistrationRateLimit.TryConsumeAsync(",
            "rotating /64s inside one /56 or /48 must hit a shared registration bucket");
        block.IndexOf("DeviceRegistrationRateLimit.TryConsumeAsync(", StringComparison.Ordinal)
            .Should().BeLessThan(block.IndexOf("INSERT INTO devices", StringComparison.Ordinal),
                "the limit must be enforced before a device row is created");

        var limiter = TestRepoPaths.ReadText("backend", "Karar.Api", "Common", "DeviceRegistrationRateLimit.cs");
        limiter.Should().Contain("foreach (var bucket in GetBuckets(ip))");
        limiter.Should().Contain("count > bucket.Limit");
    }

    [Fact]
    public void AdminDeviceBan_SuppressesTheDevicesVotes_AndUnbanRestoresThem()
    {
        var ban = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/admin/devices/{id:guid}/ban\"",
            "app.MapPost(\"/api/v1/admin/devices/{id:guid}/unban\"");
        var unban = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/admin/devices/{id:guid}/unban\"",
            "// ── AUTH ──");

        ban.Should().Contain("SetBannedDeviceVoteSuppressionAsync(connection, transaction, id, suppress: true)",
            "a banned sybil device's votes must stop counting immediately");
        unban.Should().Contain("SetBannedDeviceVoteSuppressionAsync(connection, transaction, id, suppress: false)",
            "lifting a ban restores exactly the votes the ban suppressed");
    }

    [Fact]
    public void BannedDeviceVoteSuppression_RecomputesVisibleCountersFromDbState()
    {
        var helper = SliceBlock(
            ProgramText,
            "static async Task SetBannedDeviceVoteSuppressionAsync(",
            "static async Task<VoteResponse> GetVoteResponseAsync(");

        helper.Should().Contain("suppression_reason = 'device_banned'");
        helper.Should().Contain("WHERE device_id = @deviceId AND is_suppressed = FALSE",
            "brigade-suppressed votes keep their own reason and are not touched");
        helper.Should().Contain("RETURNING post_id");
        helper.Should().Contain("await UpdateVoteCountersAsync(connection, transaction, postId, null, null);",
            "every affected post's visible counters must be recomputed in the same transaction");
    }

    // ── Behaviour: registration buckets ───────────────────────────────────

    [Fact]
    public void Ipv6Addresses_InOneSlash56_ShareTheWideBucketsButNotTheSlash64One()
    {
        var a = IPAddress.Parse("2001:db8:abcd:1200::1");
        var b = IPAddress.Parse("2001:db8:abcd:12ff::1");

        IpAddressPrivacy.ToNetworkBlock(a).Should().NotBe(IpAddressPrivacy.ToNetworkBlock(b),
            "the old per-/64 key alone let one subscriber rotate addresses freely");
        IpAddressPrivacy.ToNetworkBlock(a, 56).Should().Be("2001:db8:abcd:1200::/56")
            .And.Be(IpAddressPrivacy.ToNetworkBlock(b, 56));
        IpAddressPrivacy.ToNetworkBlock(a, 48).Should().Be("2001:db8:abcd::/48");
        IpAddressPrivacy.ToNetworkBlock(a, 64).Should().Be(IpAddressPrivacy.ToNetworkBlock(a),
            "the /64 form must keep the existing key format");
    }

    [Fact]
    public void Ipv4Registration_CountsPerIpThenTheSlash24Bucket()
    {
        var ip = IPAddress.Parse("203.0.113.42");
        var ipBucket = new DeviceRegistrationBucket(
            $"device_reg:ip:{IpAddressPrivacy.ToHashedAddressKey(ip)}", DeviceRegistrationRateLimit.PerIpLimit);

        DeviceRegistrationRateLimit.GetBuckets(ip).Should().Equal(
            ipBucket,
            new DeviceRegistrationBucket("device_reg:subnet:203.0.113.0/24", DeviceRegistrationRateLimit.PerIpv4Slash24Limit));
        DeviceRegistrationRateLimit.GetBuckets(IPAddress.Parse("::ffff:203.0.113.42")).Should().Equal(
            ipBucket,
            new DeviceRegistrationBucket("device_reg:subnet:203.0.113.0/24", DeviceRegistrationRateLimit.PerIpv4Slash24Limit));
        DeviceRegistrationRateLimit.GetBuckets(null).Should().BeEmpty();
        ipBucket.Key.Should().NotContain("203.0.113", "raw client IPs must not be written to Redis");
    }

    [Fact]
    public void Ipv6Registration_CountsTowardsSlash64Slash56AndSlash48Buckets()
    {
        var ip = IPAddress.Parse("2001:db8:abcd:1234::99");
        DeviceRegistrationRateLimit.GetBuckets(ip).Should().Equal(
            new DeviceRegistrationBucket($"device_reg:ip:{IpAddressPrivacy.ToHashedAddressKey(ip)}", 5),
            new DeviceRegistrationBucket("device_reg:subnet:2001:db8:abcd:1234::/64", 5),
            new DeviceRegistrationBucket("device_reg:v6p56:2001:db8:abcd:1200::/56", 20),
            new DeviceRegistrationBucket("device_reg:v6p48:2001:db8:abcd::/48", 50));
    }

    [Fact]
    public async Task RotatingSlash64sInsideOneSlash56_IsCappedPerHour()
    {
        // One registration from each of the 256 /64s of 2001:db8:abcd:1200::/56 (the old limit allowed all 256 × 5).
        var addresses = Enumerable.Range(0, 256)
            .Select(i => IPAddress.Parse($"2001:db8:abcd:12{i:x2}::1"));

        (await CountAllowedRegistrationsAsync(addresses)).Should().Be(DeviceRegistrationRateLimit.PerIpv6Slash56Limit);
    }

    [Fact]
    public async Task RotatingSlash56sInsideOneSlash48_IsCappedPerHour()
    {
        // Five registrations from each of 256 different /56s of 2001:db8:abcd::/48.
        var addresses = Enumerable.Range(0, 256)
            .SelectMany(i => Enumerable.Range(0, 5).Select(j => IPAddress.Parse($"2001:db8:abcd:{i:x2}{j:x2}::1")));

        (await CountAllowedRegistrationsAsync(addresses)).Should().Be(DeviceRegistrationRateLimit.PerIpv6Slash48Limit);
    }

    // Runs the same limiter the register handler calls against an in-memory Redis.
    private static async Task<int> CountAllowedRegistrationsAsync(IEnumerable<IPAddress> addresses)
    {
        var (cache, _) = InMemoryRedisDatabase.Create();
        var allowed = 0;
        foreach (var address in addresses)
        {
            if (await DeviceRegistrationRateLimit.TryConsumeAsync(cache, address)) allowed++;
        }

        return allowed;
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
