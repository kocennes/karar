using FluentAssertions;

namespace Karar.UnitTests.Moderation;

// Saldırı: zararsız bir post/yorum oluştur (moderasyondan 'active' geçer), sonra düzenleme
// penceresinde (post 15 dk, yorum 5 dk) metni TC kimlik / telefon / tehdit / kriz içeriğiyle
// değiştir. PUT uç noktaları yalnızca uzunluk doğrulaması yapıp metni doğrudan yazıyordu.
// Uç nokta mantığı Postgres olmadan çalışmadığı için bağlantı kaynak metinden doğrulanır;
// karar mantığı ContentEditModerationTests'te davranışsal olarak test edilir.
public sealed class ContentEditModerationContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void PostEdit_RunsTheCreateModerationPipelineBeforeWriting()
    {
        var block = PostEditBlock();

        block.Should().Contain("moderationService.Analyze($\"{request.Title}\\n{request.Content}\")",
            "edited title/content must pass the same keyword, PII and crisis checks as create");
        block.Should().Contain("perspectiveService.AnalyzeAsync(");
        block.Should().Contain("ContentEditModeration.Decide(moderation, perspectiveResult)");
        block.Should().Contain("InsertCrisisSupportNotificationForContentAsync(",
            "crisis language in an edit still triggers the supportive notification");
        AssertDecisionPrecedesWrite(block, "UPDATE posts");
    }

    [Fact]
    public void PostEdit_PersistsModerationStateWithTheNewText()
    {
        var block = PostEditBlock();

        block.Should().Contain("moderation_checked_at = NOW()");
        block.Should().Contain("perspective_toxicity = @perspectiveToxicity",
            "feeds filter on perspective_toxicity; a stale create-time score would keep a toxic edit in feeds");
        block.Should().Contain("(CASE WHEN user_id IS NULL THEN device_id = @deviceId ELSE user_id = @userId END)", "the account-scoped ownership predicate (sec/01-authz) must stay");
        block.Should().Contain("post_edit", "edits are logged for 5651 forensics like creates");
    }

    [Fact]
    public void CommentEdit_RunsTheCreateModerationPipelineBeforeWriting()
    {
        var block = CommentEditBlock();

        block.Should().Contain("moderationService.Analyze(request.Content)");
        block.Should().Contain("ContentEditModeration.Decide(moderation)");
        block.Should().Contain("CommentQualityScorer.Score(request.Content)");
        block.Should().Contain("InsertCrisisSupportNotificationForContentAsync(");
        AssertDecisionPrecedesWrite(block, "UPDATE comments");
    }

    [Fact]
    public void CommentEdit_PersistsModerationStateWithTheNewText()
    {
        var block = CommentEditBlock();

        block.Should().Contain("moderation_checked_at = NOW()");
        block.Should().Contain("quality_penalty = @qualityPenalty");
        block.Should().Contain("(CASE WHEN user_id IS NULL THEN device_id = @deviceId ELSE user_id = @userId END)", "the account-scoped ownership predicate (sec/01-authz) must stay");
        block.Should().Contain("comment_edit");
    }

    private static void AssertDecisionPrecedesWrite(string block, string updateStatement)
    {
        var decide = block.IndexOf("ContentEditModeration.Decide(", StringComparison.Ordinal);
        var reject = block.IndexOf("ContentEditOutcome.Rejected", StringComparison.Ordinal);
        var write = block.IndexOf(updateStatement, StringComparison.Ordinal);

        decide.Should().BeGreaterThan(0);
        reject.Should().BeGreaterThan(decide);
        write.Should().BeGreaterThan(reject, "rejected or review-requiring text must never reach the UPDATE");
    }

    private static string PostEditBlock() =>
        SliceBlock(
            ProgramText,
            "app.MapPut(\"/api/v1/posts/{id:guid}\"",
            "app.MapPost(\"/api/v1/posts/{id:guid}/save\"");

    private static string CommentEditBlock() =>
        SliceBlock(
            ProgramText,
            "app.MapPut(\"/api/v1/comments/{id:guid}\"",
            "app.MapPost(\"/api/v1/comments/{id:guid}/upvote\"");

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
