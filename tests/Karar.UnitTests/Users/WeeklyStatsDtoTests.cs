using System.Text.Json;
using FluentAssertions;
using Karar.Api.Contracts;

namespace Karar.UnitTests.Users;

public sealed class WeeklyStatsDtoTests
{
    [Fact]
    public void WeeklyStatsDto_SerializesWithFlutterContractNames()
    {
        var stats = new WeeklyStatsDto(
            WeekLabel: "2026-05-18 - 2026-05-24",
            KarmaEarned: 12,
            VotesGiven: 8,
            HakliGiven: 5,
            HaksizGiven: 3,
            PostsCreated: 2,
            CommentsPosted: 4,
            Streak: 6
        );

        var json = JsonSerializer.Serialize(stats, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"weekLabel\":\"2026-05-18 - 2026-05-24\"");
        json.Should().Contain("\"karmaEarned\":12");
        json.Should().Contain("\"votesGiven\":8");
        json.Should().Contain("\"hakliGiven\":5");
        json.Should().Contain("\"haksizGiven\":3");
        json.Should().Contain("\"postsCreated\":2");
        json.Should().Contain("\"commentsPosted\":4");
        json.Should().Contain("\"streak\":6");
    }
}
