using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

public sealed class DiscoverEventSchemaTests
{
    private static readonly string[] ExpectedEventTypes =
    [
        "impression",
        "dwell",
        "skip",
        "vote",
        "comment_open",
        "comment_reply",
        "comment_like",
        "comment_dislike",
        "save",
        "share",
        "not_interested"
    ];

    [Fact]
    public void DiscoverEventsMigration_AllowsAllProductEventTypes()
    {
        var migrationSql = TestRepoPaths.ReadText("backend", "migrations", "V41__discover_events.sql");
        var allowedTypes = Regex.Matches(migrationSql, "'([a-z_]+)'")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var missingTypes = ExpectedEventTypes
            .Where(type => !allowedTypes.Contains(type))
            .ToArray();

        missingTypes.Should().BeEmpty();
    }

    [Fact]
    public void DiscoverEventsEndpoint_IsDocumentedAndValidatesDwell()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/posts/discover/events\"",
            "app.MapGet(\"/api/v1/search\"");

        endpointBlock.Should().Contain("DWELL_SECONDS_REQUIRED");
        endpointBlock.Should().Contain("INVALID_DISCOVER_EVENT");
        endpointBlock.Should().Contain("INSERT INTO discover_events");
        endpointBlock.Should().Contain("UpsertPostViewAsync");
        endpointBlock.Should().Contain("MarkNotInterestedAsync");

        if (TestRepoPaths.TryReadText(out var apiDocs, "docs", "api.md"))
        {
            apiDocs.Should().Contain("- `POST /api/v1/posts/discover/events`");
        }
    }

    [Fact]
    public void DiscoverFeedEndpoint_KeepsAuthorAndCategoryDiversityGuardrails()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapGet(\"/api/v1/posts/discover/feed\"",
            "app.MapGet(\"/api/v1/posts/{id:guid}/stats\"");

        endpointBlock.Should().Contain("ROW_NUMBER() OVER");
        endpointBlock.Should().Contain("PARTITION BY COALESCE(p.user_id::text, p.device_id::text)");
        endpointBlock.Should().Contain("AS author_rank");
        endpointBlock.Should().Contain("WHERE author_rank <= 2");
        endpointBlock.Should().Contain("FROM post_views pv");
        endpointBlock.Should().Contain("categoryCounts");
        endpointBlock.Should().Contain("catCount >= 3");
        endpointBlock.Should().Contain("streak > 2");
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
