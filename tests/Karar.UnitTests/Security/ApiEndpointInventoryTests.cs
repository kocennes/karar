using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

public sealed class ApiEndpointInventoryTests
{
    private static readonly Regex ProgramRoutePattern = new(
        "app\\.Map(?<method>Get|Post|Put|Delete|Patch)\\(\"(?<path>[^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex DocsRoutePattern = new(
        "- `(?<route>(GET|POST|PUT|DELETE|PATCH) [^`]+)`",
        RegexOptions.Compiled);

    [Fact]
    public void DocsApi_ContainsEveryMappedEndpoint()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        if (!TestRepoPaths.TryReadText(out var docsText, "docs", "api.md"))
        {
            return;
        }

        var mappedRoutes = ProgramRoutePattern
            .Matches(programText)
            .Select(match => $"{match.Groups["method"].Value.ToUpperInvariant()} {match.Groups["path"].Value}")
            .Distinct()
            .OrderBy(route => route)
            .ToArray();

        var documentedRoutes = DocsRoutePattern
            .Matches(docsText)
            .Select(match => match.Groups["route"].Value)
            .ToHashSet(StringComparer.Ordinal);

        mappedRoutes.Should().NotBeEmpty();
        documentedRoutes.Should().Contain(
            mappedRoutes,
            "docs/api.md endpoint inventory must be updated whenever Program.cs maps a route");
    }

}
