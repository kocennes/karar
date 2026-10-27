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
        // Single kept implementation (ContentEditModeration, sec/04): rejected edits return 400 and
        // review-level edits are never written, so the previously approved text stays live and the
        // post's public comment_count does not change.
        block.Should().Contain("ContentEditModeration.Decide(moderation)");
        block.Should().Contain("ContentEditOutcome.Rejected");
        AssertReviewEditNeverReachesTheWrite(block, "UPDATE comments");
        block.Should().Contain("moderation_checked_at = NOW()");
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
        block.Should().Contain("ContentEditModeration.Decide(moderation, perspectiveResult)");
        block.Should().Contain("ContentEditOutcome.Rejected");
        AssertReviewEditNeverReachesTheWrite(block, "UPDATE posts");
        block.Should().Contain("moderation_checked_at = NOW()");
        block.Should().Contain("InsertCrisisSupportNotificationForContentAsync");

        block.IndexOf("moderationService.Analyze", StringComparison.Ordinal)
            .Should().BeLessThan(block.IndexOf("UPDATE posts", StringComparison.Ordinal));

        // Ownership / edit-window check runs before Perspective so non-owners cannot spend the quota.
        var editableCheck = block.IndexOf("SELECT EXISTS (", StringComparison.Ordinal);
        editableCheck.Should().BeGreaterThan(0);
        editableCheck.Should().BeLessThan(
            block.IndexOf("perspectiveService.AnalyzeAsync", StringComparison.Ordinal),
            "an edit by a non-owner or after the window must not reach the Perspective API");
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
        block.Should().Contain("$\"crawler:post:{id}\"",
            "the id-based crawler page is cached as well");
        block.Should().Contain("$\"post:share-image:{id}\"");
        block.Should().Contain("$\"post:story-image:{id}\"");
    }

    private static void AssertReviewEditNeverReachesTheWrite(string block, string updateStatement)
    {
        var review = block.IndexOf("if (editDecision.Outcome == ContentEditOutcome.RequiresReview)", StringComparison.Ordinal);
        var write = block.IndexOf(updateStatement, StringComparison.Ordinal);
        review.Should().BeGreaterThan(0, "a review-level edit must be handled explicitly");
        write.Should().BeGreaterThan(review);

        var reviewBranch = block[review..write];
        reviewBranch.Should().Contain("return BadRequest(editDecision.ErrorCode!, editDecision.Message);",
            "a review-level edit must return before the new text is written (it never goes live)");
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
