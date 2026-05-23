using FluentAssertions;
using Karar.Api.Services;

namespace Karar.UnitTests.Moderation;

public sealed class ReportThresholdServiceTests
{
    private readonly ReportThresholdService _service = new();

    [Fact]
    public void Evaluate_AutoHidesPost_AfterFiveUniqueReports()
    {
        var result = _service.Evaluate(
            targetType: "post",
            weightedReporterCount: 5,
            weightedCriticalCount: 0
        );

        result.ShouldAutoHide.Should().BeTrue();
        result.Priority.Should().Be("high");
    }

    [Fact]
    public void Evaluate_AutoHidesComment_AfterThreeUniqueReports()
    {
        var result = _service.Evaluate(
            targetType: "comment",
            weightedReporterCount: 3,
            weightedCriticalCount: 0
        );

        result.ShouldAutoHide.Should().BeTrue();
        result.Priority.Should().Be("medium");
    }

    [Fact]
    public void Evaluate_AutoHidesImmediately_ForCriticalReports()
    {
        var result = _service.Evaluate(
            targetType: "post",
            weightedReporterCount: 3,
            weightedCriticalCount: 3
        );

        result.ShouldAutoHide.Should().BeTrue();
        result.Priority.Should().Be("critical");
    }

    [Fact]
    public void Evaluate_KeepsPending_BelowThreshold()
    {
        var result = _service.Evaluate(
            targetType: "post",
            weightedReporterCount: 2,
            weightedCriticalCount: 0
        );

        result.ShouldAutoHide.Should().BeFalse();
        result.Priority.Should().Be("low");
    }

    [Fact]
    public void CountIndependentReporters_DiscountsSameIpBlock()
    {
        var reports = Enumerable.Range(0, 5)
            .Select(i => new ReportSignal($"device-{i}", "10.0.1.0/24", "spam"));

        _service.CountWeightedIndependentReporters(reports).Should().Be(1);
    }

    [Fact]
    public void CountIndependentReporters_RequiresDifferentFingerprintAndIpBlock()
    {
        var reports = new[]
        {
            new ReportSignal("a", "10.0.1.0/24", "spam"),
            new ReportSignal("b", "10.0.2.0/24", "spam"),
            new ReportSignal("c", "10.0.3.0/24", "spam"),
            new ReportSignal("c", "10.0.4.0/24", "spam"),
        };

        _service.CountWeightedIndependentReporters(reports).Should().Be(3);
    }
}
