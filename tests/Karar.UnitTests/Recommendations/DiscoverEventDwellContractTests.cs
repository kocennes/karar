using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Karar.Api.Common;
using Karar.Api.Contracts;
using Karar.UnitTests;

namespace Karar.UnitTests.Recommendations;

// POST /api/v1/posts/discover/events stores dwell clamped to 3..600. The Flutter client reports the whole
// time spent on a Discover card (any value >= 3, no upper cap). Once RequestValidation started enforcing
// record-parameter DataAnnotations, a [Range(3, 600)] on DwellSeconds turned a reader who stayed past
// 10 minutes into a 400 (event lost) instead of a stored 600, and left the clamp as dead code.
public sealed class DiscoverEventDwellContractTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(45)]
    [InlineData(601)]
    [InlineData(3600)]
    public void DwellOutsideTheStoredRange_IsNotRejectedByValidation(int seconds)
    {
        var results = new List<ValidationResult>();

        RequestValidation.TryValidate(new DiscoverEventRequest(Guid.NewGuid(), "dwell", seconds), results)
            .Should().BeTrue("the handler clamps dwell instead of rejecting it");
    }

    [Fact]
    public void Handler_ClampsDwellBeforeStoringIt()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        const string start = "app.MapPost(\"/api/v1/posts/discover/events\", async (";
        var from = program.IndexOf(start, StringComparison.Ordinal);
        from.Should().BeGreaterThanOrEqualTo(0);
        var to = program.IndexOf("\napp.Map", from + start.Length, StringComparison.Ordinal);
        var block = to < 0 ? program[from..] : program[from..to];

        block.Should().Contain("Math.Clamp(seconds, 3, 600)");
    }
}
