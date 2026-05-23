using System.Text.RegularExpressions;
using FluentAssertions;

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
        var root = FindRepoRoot();
        var programText = File.ReadAllText(Path.Combine(root, "backend", "Karar.Api", "Program.cs"));
        var docsText = File.ReadAllText(Path.Combine(root, "docs", "api.md"));

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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TODO.md"))
                && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root could not be found.");
    }
}
