using FluentAssertions;

namespace Karar.UnitTests.Notifications;

public sealed class NotificationEndpointAnalyticsTests
{
    [Theory]
    [InlineData("app.MapPut(\"/api/v1/notifications/read-all\"", "app.MapGet(\"/api/v1/notifications/unread-count\"", "'read'")]
    [InlineData("app.MapPut(\"/api/v1/notifications/{id:guid}/read\"", "app.MapPost(\"/api/v1/notifications/{id:guid}/dismiss\"", "'read'")]
    [InlineData("app.MapPost(\"/api/v1/notifications/{id:guid}/dismiss\"", "app.MapPost(\"/api/v1/notifications/clear-read\"", "'dismissed'")]
    [InlineData("app.MapPost(\"/api/v1/notifications/clear-read\"", "app.MapPost(\"/api/v1/notifications/mute\"", "'dismissed'")]
    public void NotificationMutationEndpoints_RecordLifecycleAnalyticsEvents(
        string startMarker,
        string endMarker,
        string eventType)
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var endpointBlock = SliceEndpointBlock(programText, startMarker, endMarker);

        endpointBlock.Should().Contain("WITH updated AS");
        endpointBlock.Should().Contain("RETURNING id, device_id");
        endpointBlock.Should().Contain("INSERT INTO notification_events");
        endpointBlock.Should().Contain("notification_id, device_id, event_type");
        endpointBlock.Should().Contain(eventType);
    }

    [Fact]
    public void NotificationOpenedEndpoint_RecordsOpenedLifecycleAnalyticsEvent()
    {
        var programText = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");
        var endpointBlock = SliceEndpointBlock(
            programText,
            "app.MapPost(\"/api/v1/notifications/{id:guid}/opened\"",
            "app.MapPost(\"/api/v1/notifications/{id:guid}/dismiss\"");

        endpointBlock.Should().Contain("INSERT INTO notification_events");
        endpointBlock.Should().Contain("notification_id, device_id, event_type");
        endpointBlock.Should().Contain("'opened'");
        endpointBlock.Should().Contain("dismissed_at IS NULL");
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
