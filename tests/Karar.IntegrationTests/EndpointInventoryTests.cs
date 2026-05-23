using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Karar.IntegrationTests;

public class EndpointInventoryTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly ITestOutputHelper _output;

    public EndpointInventoryTests(CustomWebApplicationFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    [Fact]
    public void VerifyEndpointInventoryMatchesDocumentation()
    {
        // Get all registered endpoints from the running app
        using var scope = _factory.Services.CreateScope();
        var apiExplorer = scope.ServiceProvider.GetRequiredService<IApiDescriptionGroupCollectionProvider>();

        var actualEndpoints = apiExplorer.ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .Select(api => $"{api.HttpMethod} {api.RelativePath}")
            .OrderBy(s => s)
            .ToList();

        _output.WriteLine("Aktif Endpointler:");
        foreach (var endpoint in actualEndpoints)
        {
            _output.WriteLine($"- {endpoint}");
        }

        // Read the expected inventory from docs/api.md
        var apiMdPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "docs", "api.md");
        if (!File.Exists(apiMdPath))
        {
            // Fallback for different test execution paths
            apiMdPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "docs", "api.md");
        }

        if (File.Exists(apiMdPath))
        {
            var mdContent = File.ReadAllText(apiMdPath);
            var expectedLines = mdContent
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("- `") && l.EndsWith("`"))
                .Select(l => l.Replace("- `", "").Replace("`", ""))
                .OrderBy(s => s)
                .ToList();

            // Check for shadow endpoints (in code but not in docs)
            var shadowEndpoints = actualEndpoints.Except(expectedLines).ToList();

            // Check for missing documentation (in docs but not in code)
            var missingEndpoints = expectedLines.Except(actualEndpoints).ToList();

            foreach (var shadow in shadowEndpoints)
            {
                _output.WriteLine($"DOKÜMANDA EKSİK: {shadow}");
            }

            foreach (var missing in missingEndpoints)
            {
                _output.WriteLine($"KODDA EKSİK: {missing}");
            }

            // Assert that there are no shadow endpoints
            // We allow missing endpoints in code (placeholders in docs), but NOT shadow endpoints in code.
            Assert.Empty(shadowEndpoints);
        }
        else
        {
            _output.WriteLine("UYARI: docs/api.md bulunamadı, karşılaştırma atlandı.");
        }
    }
}
