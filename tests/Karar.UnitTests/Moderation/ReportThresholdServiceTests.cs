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

    // P0-02: 'minor' (a possibly under-age user) reaches a moderator first, but does not lower the
    // auto-hide bar: it is a claim about the author that other reporters cannot check.
    [Fact]
    public void MinorReports_GetQueuePriority_ButDoNotCountTowardCriticalAutoHide()
    {
        ReportThresholdService.PriorityQueueReasons.Should().BeEquivalentTo(["self_harm", "illegal", "minor"]);
        _service.IsCriticalReason("minor").Should().BeFalse();
        _service.IsCriticalReason("self_harm").Should().BeTrue();
        _service.IsCriticalReason("illegal").Should().BeTrue();

        // Two maximum-reputation reporters (weight 2.0 each) choosing 'minor' on a post: below the
        // general post threshold (5), and 'minor' adds nothing to the critical count.
        var result = _service.Evaluate("post", weightedReporterCount: 4, weightedCriticalCount: 0);
        result.ShouldAutoHide.Should().BeFalse();
    }

    [Fact]
    public void AdminReportQueue_OrdersPriorityReasonsFirst()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        program.Should().Contain("ORDER BY (r.reason = ANY(@priorityReasons)) DESC, r.created_at ASC, r.id");
        program.Should().Contain("command.Parameters.AddWithValue(\"priorityReasons\", ReportThresholdService.PriorityQueueReasons);");
    }
}
