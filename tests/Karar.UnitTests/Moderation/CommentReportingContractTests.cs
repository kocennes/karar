using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

public sealed class CommentReportingContractTests
{
    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static readonly string ReportBottomSheetText =
        TestRepoPaths.ReadText("lib", "features", "report", "report_bottom_sheet.dart");

    private static readonly string CommentListText =
        TestRepoPaths.ReadText("lib", "features", "post_detail", "comment_list.dart");

    private static string ReportEndpointBlock => SliceBlock(
        ProgramText,
        "app.MapPost(\"/api/v1/reports\"",
        "app.MapPost(\"/api/v1/feedback\"");

    [Fact]
    public void ReportEndpoint_AcceptsCommentTargets()
    {
        ReportEndpointBlock.Should().Contain("request.TargetType is not (\"post\" or \"comment\")",
            because: "the shared report endpoint must accept comment reports from the comment menu");
        ReportEndpointBlock.Should().Contain("ReportTargetExistsAsync(connection, transaction, request.TargetType, request.TargetId)");
        ProgramText.Should().Contain("var table = targetType == \"post\" ? \"posts\" : \"comments\"",
            because: "target existence and auto-hide must route comment targets to the comments table");
    }

    [Fact]
    public void ReportEndpoint_ReasonAllowlistMatchesReportBottomSheet()
    {
        foreach (var reason in new[]
                 {
                     "hate_speech",
                     "harassment",
                     "personal_info",
                     "misinformation",
                     "spam",
                     "self_harm",
                     "illegal",
                     "other"
                 })
        {
            ReportBottomSheetText.Should().Contain($"'{reason}'",
                because: $"UI exposes report reason {reason}");
            ReportEndpointBlock.Should().Contain($"\"{reason}\"",
                because: $"backend must accept report reason {reason}");
        }
    }

    [Fact]
    public void CommentList_OpensReportBottomSheetForCommentTargets()
    {
        CommentListText.Should().Contain("ReportBottomSheet.show");
        CommentListText.Should().Contain("targetType: 'comment'");
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
