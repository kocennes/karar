using FluentAssertions;

namespace Karar.UnitTests.Recommendations;

/// <summary>
/// Contract tests for the feed negative feedback reason system (B9-9 P1).
/// Verifies that the /api/v1/posts/{id}/feedback endpoint:
///  - accepts an optional Reason field
///  - validates reason against the allowed vocabulary
///  - persists reason-tagged events to discover_events for admin analytics
/// </summary>
public sealed class FeedNegativeFeedbackReasonTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly string RequestsText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Contracts", "Requests.cs");

    private static string FeedbackEndpointBlock => SliceBlock(
        ProgramText,
        "app.MapPost(\"/api/v1/posts/{id:guid}/feedback\"",
        "app.MapPost(\"/api/v1/posts/discover/events\"");

    // ── Contract: PostFeedbackRequest.Reason field ───────────────────────────

    [Fact]
    public void PostFeedbackRequest_HasOptionalReasonField()
    {
        RequestsText.Should().Contain("string? Reason",
            because: "PostFeedbackRequest must expose an optional Reason field for detailed feedback categorisation");
    }

    // ── Contract: allowed reason vocabulary ─────────────────────────────────

    [Theory]
    [InlineData("toksik")]
    [InlineData("tekrarlı")]
    [InlineData("ilgilenmiyorum")]
    [InlineData("siyasi_fazla")]
    [InlineData("kalitesiz_yorum")]
    public void FeedbackEndpoint_AcceptsAllDefinedReasons(string reason)
    {
        FeedbackEndpointBlock.Should().Contain($"\"{reason}\"",
            because: $"reason '{reason}' must be in the allowed-reason validation list");
    }

    [Fact]
    public void FeedbackEndpoint_RejectsUnknownReasonWithBadRequest()
    {
        FeedbackEndpointBlock.Should().Contain("INVALID_FEEDBACK_REASON",
            because: "an unknown reason value must result in a 400 INVALID_FEEDBACK_REASON response");
    }

    // ── Contract: analytics persistence ─────────────────────────────────────

    [Fact]
    public void FeedbackEndpoint_PersistsReasonToDiscoverEvents()
    {
        FeedbackEndpointBlock.Should().Contain("discover_events",
            because: "when a reason is provided the event must be recorded in discover_events for admin analytics");
    }

    [Fact]
    public void FeedbackEndpoint_StoresFeedbackReasonInMetadata()
    {
        FeedbackEndpointBlock.Should().Contain("feedback_reason",
            because: "the reason must be stored under the 'feedback_reason' key in the metadata JSON column");
    }

    [Fact]
    public void FeedbackEndpoint_InsertsNotInterestedEventType()
    {
        FeedbackEndpointBlock.Should().Contain("'not_interested'",
            because: "the discover_events row must use event_type 'not_interested' so existing analytics queries pick it up");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end < 0) return text[start..];
        return text[start..end];
    }
}
