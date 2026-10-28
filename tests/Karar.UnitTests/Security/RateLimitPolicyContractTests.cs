using System.Text.RegularExpressions;
using FluentAssertions;

namespace Karar.UnitTests.Security;

public sealed class RateLimitPolicyContractTests
{
    // RequireRateLimiting("x") with no options.AddPolicy("x") compiles, but the RateLimitingMiddleware throws
    // InvalidOperationException on every request to that endpoint, i.e. a 500 for all callers.
    // POST /api/v1/feedback ("api") and POST /api/v1/posts/{id}/ai-summary ("fixed") shipped that way.
    [Fact]
    public void EveryRequiredRateLimitPolicy_IsRegistered()
    {
        var programText = File.ReadAllText(TestRepoPaths.FilePath("backend", "Karar.Api", "Program.cs"));

        var registered = Regex.Matches(programText, @"options\.AddPolicy\(\s*""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var required = Regex.Matches(programText, @"RequireRateLimiting\(\s*""([^""]+)""\s*\)")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        required.Should().NotBeEmpty();
        required.Except(registered).Should().BeEmpty(
            because: "an endpoint that requires an unregistered rate limiter policy fails every request with a 500");
    }
}
