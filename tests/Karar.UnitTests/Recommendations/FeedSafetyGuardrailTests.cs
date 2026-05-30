using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

public sealed class FeedSafetyGuardrailTests
{
    [Fact]
    public void FeedMainQuery_ContainsSafetyGuardrails()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/posts\", async (",
            "var freshSlotTarget");

        VerifySafetyGuardrails(endpointBlock);
    }

    [Fact]
    public void FreshSlotQuery_ContainsSafetyGuardrails()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "await using var freshCmd = new NpgsqlCommand(",
            "var freshPosts = LabelPosts(await ReadPostsAsync(freshCmd)");

        VerifySafetyGuardrails(endpointBlock);
    }

    [Fact]
    public void SerendipityQuery_ContainsSafetyGuardrails()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "await using var serendipityCmd = new NpgsqlCommand(",
            "var serendipityPosts = LabelPosts(await ReadPostsAsync(serendipityCmd)");

        VerifySafetyGuardrails(endpointBlock);

        // Serendipity specific
        endpointBlock.Should().Contain("NOT EXISTS (SELECT 1 FROM muted_categories mc");
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
}
