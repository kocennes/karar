using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Metrics;

public sealed class VoteTimelineBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 27, 14, 37, 12, DateTimeKind.Utc);

    [Fact]
    public void ZeroFillHourly_ReturnsTwentyFourZeros_WhenNoVotes()
    {
        var timeline = VoteTimelineBuilder.ZeroFillHourly(
            Array.Empty<(DateTime Hour, int Count)>(),
            Now);

        timeline.Should().HaveCount(24);
        timeline.Should().OnlyContain(count => count == 0);
    }

    [Fact]
    public void ZeroFillHourly_PlacesCurrentHourLast_AndOldestHourFirst()
    {
        var currentHour = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);
        var oldestHour = currentHour.AddHours(-23);

        var timeline = VoteTimelineBuilder.ZeroFillHourly(
            new[] { (currentHour, 7), (oldestHour, 3) },
            Now);

        timeline.Should().HaveCount(24);
        timeline[23].Should().Be(7);
        timeline[0].Should().Be(3);
        timeline.Sum().Should().Be(10);
    }

    [Fact]
    public void ZeroFillHourly_IgnoresBucketsOutsideTheWindow()
    {
        var currentHour = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);

        var timeline = VoteTimelineBuilder.ZeroFillHourly(
            new[]
            {
                (currentHour.AddHours(-24), 5), // bir saat fazla eski
                (currentHour.AddHours(1), 9),   // gelecekte (saat kayması)
                (currentHour.AddHours(-5), 2),
            },
            Now);

        timeline.Sum().Should().Be(2);
        timeline[18].Should().Be(2);
    }

    [Fact]
    public void ZeroFillHourly_SumsDuplicateBuckets_AndIgnoresKind()
    {
        var hourUtc = new DateTime(2026, 10, 27, 10, 0, 0, DateTimeKind.Utc);
        var hourUnspecified = new DateTime(2026, 10, 27, 10, 15, 0, DateTimeKind.Unspecified);

        var timeline = VoteTimelineBuilder.ZeroFillHourly(
            new[] { (hourUtc, 4), (hourUnspecified, 6) },
            Now);

        timeline[19].Should().Be(10);
        timeline.Sum().Should().Be(10);
    }

    [Fact]
    public void ZeroFillHourly_HonoursCustomWindowLength()
    {
        var currentHour = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);

        var timeline = VoteTimelineBuilder.ZeroFillHourly(
            new[] { (currentHour, 1), (currentHour.AddHours(-6), 1) },
            Now,
            hours: 6);

        timeline.Should().HaveCount(6);
        timeline.Sum().Should().Be(1);
        timeline[5].Should().Be(1);
    }

    [Fact]
    public void ZeroFillHourly_ClampsNegativeCounts()
    {
        var currentHour = new DateTime(2026, 10, 27, 14, 0, 0, DateTimeKind.Utc);

        var timeline = VoteTimelineBuilder.ZeroFillHourly(
            new[] { (currentHour, -3) },
            Now);

        timeline[23].Should().Be(0);
    }
}
