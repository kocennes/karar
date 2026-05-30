using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

public sealed class DiscoverSafetyGuardrailTests
{
    [Fact]
    public void DiscoverFeedEndpoint_ContainsSafetyGuardrails()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/posts/discover/feed\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

        VerifySafetyGuardrails(endpointBlock);

        endpointBlock.Should().Contain("p.distribution_stage >= 2");
        endpointBlock.Should().Contain("blocked_users bu");
        endpointBlock.Should().Contain("muted_categories mc");
    }

    [Fact]
    public void DiscoverSections_ContainSafetyGuardrails()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/posts/discover\", async (",
            "IReadOnlyList<PostDto>? cityTrending = null;");

        // Checks Rising, Controversial, Fresh (collectively)
        VerifySafetyGuardrails(endpointBlock);
        endpointBlock.Should().Contain("p.distribution_stage >= 2");
        CountOccurrences(endpointBlock, "blocked_users bu").Should().BeGreaterThanOrEqualTo(3,
            "rising, controversial and fresh discover pools must exclude blocked authors for signed-in users");
        CountOccurrences(endpointBlock, "muted_categories mc").Should().BeGreaterThanOrEqualTo(3,
            "rising, controversial and fresh discover pools must exclude muted categories for signed-in users");
    }

    [Fact]
    public void CityTrendingQuery_ContainsSafetyGuardrails()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "await using var cityCmd = new NpgsqlCommand(",
            "cityTrending = LabelPosts(await ReadPostsAsync(cityCmd)");

        VerifySafetyGuardrails(endpointBlock);
        endpointBlock.Should().Contain("blocked_users bu");
        endpointBlock.Should().Contain("muted_categories mc");
    }

    private static void VerifySafetyGuardrails(string block)
    {
        block.Should().Contain("p.status = 'active'");
        block.Should().Contain("p.is_unlisted = FALSE");
        block.Should().Contain("p.perspective_toxicity IS NULL OR p.perspective_toxicity < 0.6");
        block.Should().Contain("p.image_moderation_status IS NULL OR p.image_moderation_status = 'approved'");
        block.Should().Contain("SELECT COUNT(*) FROM reports r");
        block.Should().Contain("< 3");
    }

    private static string SliceEndpointBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");

        return text[start..end];
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }
}
