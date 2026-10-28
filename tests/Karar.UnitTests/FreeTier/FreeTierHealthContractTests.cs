using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.Api.Common.Middleware;
using Microsoft.AspNetCore.Http;

namespace Karar.UnitTests.FreeTier;

/// Source contract for the free-tier health surface: an uptime ping every 5 minutes must keep
/// Render awake without waking Neon, so "/health" and "/health/live" are the same DB-free check.
public sealed class FreeTierHealthContractTests
{
    private static string Program() => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void SelfCheck_IsTheOnlyLiveTaggedCheck_AndIsDbFree()
    {
        var program = Program();

        var liveTags = Regex.Matches(program, "tags: \\[\"live\"\\]");
        liveTags.Should().HaveCount(1, "exactly one health check may carry the 'live' tag");

        var liveLine = program[..liveTags[0].Index];
        liveLine = liveLine[(liveLine.LastIndexOf('\n') + 1)..];
        liveLine.Should().Contain(".AddCheck(\"self\", () => HealthCheckResult.Healthy()",
            "the live check must be the in-process self check, never Npgsql or Redis");

        program.Should().Contain("AddNpgSql(postgresCs, name: \"postgres\", tags: [\"ready\", \"db\"])");
        program.Should().Contain("name: \"redis\", tags: [\"ready\", \"cache\"]");
    }

    [Fact]
    public void HealthAlias_SharesTheLiveOptionsWithHealthLive()
    {
        var program = Program();

        program.Should().Contain("var liveHealthOptions = new HealthCheckOptions");
        Regex.Matches(program, "Predicate = c => c\\.Tags\\.Contains\\(\"live\"\\)")
            .Should().HaveCount(1, "the live predicate is defined once and shared, not duplicated");

        program.Should().Contain("app.MapHealthChecks(\"/health\", liveHealthOptions);");
        program.Should().Contain("app.MapHealthChecks(\"/health/live\", liveHealthOptions);");

        program.Should().Contain("app.MapHealthChecks(\"/health/ready\"");
        program.Should().Contain("Predicate = c => c.Tags.Contains(\"ready\")",
            "readiness keeps its own DB-backed predicate");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public void ActivityGate_IgnoresTheHealthAlias(string path)
    {
        ActivityTrackingMiddleware.CountsAsUserActivity(new PathString(path), "UptimeRobot/2.0")
            .Should().BeFalse("the external keepalive ping must not end idleness");
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    [InlineData("/HEALTH")]
    public void SubnetBanCheck_IsSkippedForHealthPaths(string path)
    {
        // SubnetBanMiddleware runs in front of the health endpoints. Its Redis ban cache lives 1 minute,
        // the keepalive pings every 5-10 minutes from a public IP, so every ping would miss the cache and
        // run SELECT ... FROM banned_subnets on Postgres: the DB-free /health would wake Neon after all.
        SubnetBanMiddleware.SkipsBanCheck(new PathString(path)).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/v1/posts")]
    [InlineData("/posts/3fa85f64-komsu-gurultusu")]
    [InlineData("/")]
    public void SubnetBanCheck_StillAppliesToEverythingElse(string path)
    {
        SubnetBanMiddleware.SkipsBanCheck(new PathString(path)).Should().BeFalse();
    }

    [Fact]
    public void SubnetBanMiddleware_ConsultsTheExemption_BeforeTheBanLookup()
    {
        var source = TestRepoPaths.ReadText("backend", "Karar.Api", "Common", "Middleware", "SubnetBanMiddleware.cs");

        var exemption = source.IndexOf("SkipsBanCheck(context.Request.Path)", StringComparison.Ordinal);
        var lookup = source.IndexOf("subnetBan.IsBannedAsync(", StringComparison.Ordinal);
        exemption.Should().BePositive("the health exemption must be wired into InvokeAsync");
        exemption.Should().BeLessThan(lookup, "the exemption must short-circuit before the Redis/Postgres lookup");
    }

    [Fact]
    public void SmokeScript_WaitsForLiveness_ThenAsksReadinessOnce()
    {
        var smoke = TestRepoPaths.ReadText("scripts", "smoke-api.ps1");

        smoke.Should().Contain("\"$BaseUrl/health/live\"", "the cold-start retry loop is a liveness question");
        smoke.Should().Contain("\"$BaseUrl/health/ready\"", "a smoke test does need DB readiness before hitting the API");

        var live = smoke.IndexOf("$BaseUrl/health/live", StringComparison.Ordinal);
        var ready = smoke.IndexOf("$BaseUrl/health/ready", StringComparison.Ordinal);
        live.Should().BeLessThan(ready, "liveness is established before readiness is asked");

        var readyHits = Regex.Matches(smoke, Regex.Escape("$BaseUrl/health/ready"));
        readyHits.Should().HaveCount(1, "readiness is asked once, not inside the retry loop");
    }

    [Fact]
    public void CiWorkflow_PollsLivenessBeforeReadiness_InEveryDeployGate()
    {
        var ci = TestRepoPaths.ReadText(".github", "workflows", "ci.yml");

        // Only curl targets count (echo lines also mention the paths).
        const string curlTarget = "/health/{0}\" || echo \"000\")";
        var probes = Regex.Matches(ci, Regex.Escape(string.Format(curlTarget, "live")))
            .Select(m => (Index: m.Index, Probe: "live"))
            .Concat(Regex.Matches(ci, Regex.Escape(string.Format(curlTarget, "ready")))
                .Select(m => (Index: m.Index, Probe: "ready")))
            .OrderBy(p => p.Index)
            .Select(p => p.Probe)
            .ToList();

        probes.Should().NotBeEmpty("post-deploy gates still verify DB readiness");
        probes.Count(p => p == "ready").Should().Be(2, "staging and production each keep one readiness gate");
        probes.Should().Equal(["live", "ready", "live", "ready"],
            "each readiness poll is preceded by a DB-free liveness poll so a cold-starting instance is told apart from a DB outage");
    }

    [Fact]
    public void Readme_DocumentsRunningOnTheFreeTier()
    {
        var readme = TestRepoPaths.ReadText("README.md");

        readme.Should().Contain("## Ucretsiz Katmanda Calistirma");
        readme.Should().Contain("FreeTier__Enabled");
        readme.Should().Contain("FreeTier__IdleAfterMinutes");
        readme.Should().Contain("/health");
        readme.Should().Contain("backend/docker-compose.yml");
        readme.Should().MatchRegex("cron-job\\.org|UptimeRobot", "the owner must set up an external ping");
        readme.Should().NotMatchRegex("(?i)password=|api[_-]?key=", "no secrets in the README");
    }
}
