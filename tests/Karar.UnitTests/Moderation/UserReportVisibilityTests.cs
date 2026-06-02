using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Moderation;

public sealed class UserReportVisibilityTests
{
    [Fact]
    public void UserReportsEndpoint_ReturnsOnlyOwnReportsWithSafeStatuses()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var block = Slice(
            program,
            "app.MapGet(\"/api/v1/users/me/reports\"",
            "app.MapPost(\"/api/v1/users/me/moderation-appeals\"");

        block.Should().Contain("GetJwtPrincipal(httpRequest, jwtService)");
        block.Should().Contain("r.reporter_user_id = @userId OR r.reporter_device_id = @deviceId");
        block.Should().Contain("WHEN 'pending'      THEN 'alındı'");
        block.Should().Contain("WHEN 'under_review' THEN 'inceleniyor'");
        block.Should().Contain("WHEN 'actioned'     THEN 'işlem yapıldı'");
        block.Should().Contain("WHEN 'dismissed'    THEN 'reddedildi'");
        block.Should().Contain("İnceleme sonunda bu içerikte işlem gerektiren bir ihlal bulunmadı.");

        var apiDocs = TestRepoPaths.ReadText("docs", "api.md");
        apiDocs.Should().Contain("- `GET /api/v1/users/me/reports`");
    }

    [Fact]
    public void ModerationHistoryScreen_ShowsReportStatuses()
    {
        var auth = TestRepoPaths.ReadText("lib", "core", "auth", "auth_service.dart");
        var screen = TestRepoPaths.ReadText("lib", "features", "settings", "moderation_history_screen.dart");

        auth.Should().Contain("fetchReportHistory");
        auth.Should().Contain("ReportHistoryItem");
        auth.Should().Contain("publicStatus");
        auth.Should().Contain("publicReason");
        screen.Should().Contain("reportHistoryProvider");
        screen.Should().Contain("_ReportTile");
        screen.Should().Contain("Karar: $actionLabel");
        screen.Should().Contain("_decisionLabel");
        screen.Should().Contain("_targetLabel(event.targetType)");
        screen.Should().Contain("report.publicStatus");
        screen.Should().Contain("report.publicReason");
    }

    private static string Slice(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);
        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex);
        return text[startIndex..endIndex];
    }
}
