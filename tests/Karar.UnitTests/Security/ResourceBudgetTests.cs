using FluentAssertions;

namespace Karar.UnitTests.Security;

public sealed class ResourceBudgetTests
{
    [Fact]
    public void SearchEndpoint_EnforcesPageSizeAndCommandTimeout()
    {
        var programText = ReadProgram();
        var searchBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/search\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/vote\"");

        searchBlock.Should().Contain("limit = Math.Clamp(limit, 1, 50);");
        searchBlock.Should().Contain("command.CommandTimeout = 5;");
        searchBlock.Should().Contain("EnforceMinimumResponseTimeAsync(responseTimer");
        programText.Should().Contain("TimeSpan.FromMilliseconds(200)");
    }

    [Fact]
    public void DataExportEndpoint_EnforcesDailyBudgetAndTimeout()
    {
        var programText = ReadProgram();
        var exportBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/users/me/data-export\"",
            "app.MapPost(\"/api/v1/users/me/blocked\"");

        exportBlock.Should().Contain("redis.IsAllowedAsync(");
        exportBlock.Should().Contain("\"data-export\"");
        exportBlock.Should().Contain("limit: 1");
        exportBlock.Should().Contain("TimeSpan.FromDays(1)");
        exportBlock.Should().Contain("DATA_EXPORT_LIMIT");
        exportBlock.Should().Contain("command.CommandTimeout = 10;");
    }

    private static string ReadProgram()
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine(root, "backend", "Karar.Api", "Program.cs"));
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
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
