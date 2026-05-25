using FluentAssertions;

namespace Karar.UnitTests.Recommendations;

public sealed class DiscoverSafetyGuardrailTests
{
    [Fact]
    public void DiscoverFeedEndpoint_ContainsSafetyGuardrails()
    {
        var programText = File.ReadAllText(FindRepoFile("backend/Karar.Api/Program.cs"));

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/posts/discover/feed\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

        // Existing guardrails
        endpointBlock.Should().Contain("p.status = 'active'");
        endpointBlock.Should().Contain("p.is_unlisted = FALSE");
        endpointBlock.Should().Contain("p.distribution_stage >= 2");

        // New safety guardrails
        endpointBlock.Should().Contain("p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6");
        endpointBlock.Should().Contain("p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved'");
        endpointBlock.Should().Contain("SELECT COUNT(*) FROM reports r");
        endpointBlock.Should().Contain("r.target_type = 'post'");
        endpointBlock.Should().Contain("r.status = 'pending'");
        endpointBlock.Should().Contain("< 3");
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file: {relativePath}");
    }
}
