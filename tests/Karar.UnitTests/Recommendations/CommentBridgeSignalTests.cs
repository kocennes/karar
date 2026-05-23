using System.Text.Json;
using FluentAssertions;
using Karar.Api.Models;

namespace Karar.UnitTests.Recommendations;

public sealed class CommentBridgeSignalTests
{
    [Fact]
    public void CommentDto_SerializesVoteGroupUpvoteSignals()
    {
        var comment = new CommentDto(
            Id: Guid.NewGuid(),
            Content: "Dengeli bir gerekçe.",
            UpvoteCount: 18,
            DownvoteCount: 2,
            MyUpvote: false,
            MyDownvote: false,
            IsOwner: false,
            CreatedAt: DateTimeOffset.UtcNow,
            UpvotesHakli: 9,
            UpvotesHaksiz: 7,
            Reactions: new Dictionary<string, int> { ["👍"] = 3 },
            MyReaction: "👍"
        );

        var json = JsonSerializer.Serialize(comment, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"upvotesHakli\":9");
        json.Should().Contain("\"upvotesHaksiz\":7");
        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty("reactions").GetProperty("👍").GetInt32().Should().Be(3);
        document.RootElement.GetProperty("myReaction").GetString().Should().Be("👍");
    }
}
