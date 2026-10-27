using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

// Regression: PUT /comments/{id} and PUT /posts/{id} wrote the new text straight to the DB.
// A harmless comment/post that passed create-time moderation could be edited within the
// edit window to carry a victim's TC Kimlik / IBAN / phone number or slurs and stay active.
public sealed class EditModerationTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private readonly ContentModerationService _moderation = new();

    // Behavioural half: the payloads an edit used to let through are exactly what the
    // create-time pipeline blocks or holds for review.
    [Theory]
    [InlineData("Adresi belli, TC kimlik numarası 12345678901")]
    [InlineData("Paraları şu hesaba yatırın: TR33 0006 1005 1978 6457 8413 26")]
    public void Analyze_RejectsDoxxingPayloadsUsedInEditAttack(string editedText)
    {
        var decision = _moderation.Analyze(editedText);

        decision.IsRejected.Should().BeTrue();
        decision.Code.Should().Be("CONTENT_REJECTED");
    }

    // Contract half: both edit handlers must run that pipeline before writing.
    [Fact]
    public void CommentEdit_RunsModerationBeforeWriting()
    {
        var block = Slice(
            ProgramText,
            "app.MapPut(\"/api/v1/comments/{id:guid}\"",
            "app.MapPost(\"/api/v1/comments/{id:guid}/upvote\"");

        block.Should().Contain("ContentModerationService moderationService");
        block.Should().Contain("moderationService.Analyze(request.Content)");
        block.Should().Contain("if (moderation.IsRejected)");
        block.Should().Contain("status = @status",
            "an edit that needs review must take the comment out of the active set");
        block.Should().Contain("moderation_checked_at = NOW()");
        block.Should().Contain("comment_count = GREATEST(0, comment_count - 1)",
            "a comment moved to review must leave the post's public comment count");
        block.Should().Contain("InsertCrisisSupportNotificationForContentAsync");

        block.IndexOf("moderationService.Analyze", StringComparison.Ordinal)
            .Should().BeLessThan(block.IndexOf("UPDATE comments", StringComparison.Ordinal),
                "moderation must run before the new content is stored");
    }

    [Fact]
    public void PostEdit_RunsModerationAndPerspectiveBeforeWriting()
    {
        var block = Slice(
            ProgramText,
            "app.MapPut(\"/api/v1/posts/{id:guid}\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/save\"");

        block.Should().Contain("ContentModerationService moderationService");
        block.Should().Contain("PerspectiveApiService perspectiveService");
        block.Should().Contain("moderationService.Analyze($\"{request.Title}\\n{request.Content}\")");
        block.Should().Contain("perspectiveService.AnalyzeAsync($\"{request.Title}\\n{request.Content}\")");
        block.Should().Contain("if (moderation.IsRejected)");
        block.Should().Contain("status = @status");
        block.Should().Contain("moderation_checked_at = NOW()");
        block.Should().Contain("InsertCrisisSupportNotificationForContentAsync");

        block.IndexOf("moderationService.Analyze", StringComparison.Ordinal)
            .Should().BeLessThan(block.IndexOf("UPDATE posts", StringComparison.Ordinal));
    }

    [Fact]
    public void PostEdit_InvalidatesShareAndCrawlerCaches()
    {
        var block = Slice(
            ProgramText,
            "app.MapPut(\"/api/v1/posts/{id:guid}\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/save\"");

        block.Should().Contain("$\"crawler:slug:{",
            "OG HTML for crawlers must not keep serving the pre-edit text");
        block.Should().Contain("$\"post:share-image:{id}\"");
        block.Should().Contain("$\"post:story-image:{id}\"");
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"marker '{start}' must exist");
        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"marker '{end}' must follow '{start}'");
        return text[startIndex..endIndex];
    }
}
