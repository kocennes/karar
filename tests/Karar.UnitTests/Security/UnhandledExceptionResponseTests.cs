using System.Text.Json;
using FluentAssertions;
using Karar.Api.Common;

namespace Karar.UnitTests.Security;

/// Behavioural tests for the body the global exception middleware writes: outside Development
/// the client gets a generic Turkish message and a traceId only; in Development the exception
/// message and ToString() are kept so local debugging still works.
public sealed class UnhandledExceptionResponseTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private const string LeakyMessage =
        "28P01: password authentication failed (Host=db.internal.karar;Username=karar_app;Password=hunter2)";

    [Fact]
    public void BuildErrorBody_OutsideDevelopment_ReturnsGenericMessageAndTraceIdWithoutDetail()
    {
        var ex = CaptureThrown();

        var body = UnhandledExceptionResponse.BuildErrorBody(isDevelopment: false, ex, "00-abc-01");

        body.Error.Code.Should().Be("INTERNAL_ERROR");
        body.Error.Message.Should().Be("Beklenmeyen bir hata oluştu. Lütfen daha sonra tekrar deneyin.");
        body.Error.TraceId.Should().Be("00-abc-01");
        body.Error.Detail.Should().BeNull();
    }

    [Fact]
    public void BuildErrorBody_OutsideDevelopment_SerializedJsonLeaksNothing()
    {
        var ex = CaptureThrown();

        var json = JsonSerializer.Serialize(
            UnhandledExceptionResponse.BuildErrorBody(isDevelopment: false, ex, "trace-1"), WebJson);

        json.Should().NotContain("hunter2");
        json.Should().NotContain("db.internal");
        json.Should().NotContain("28P01");
        json.Should().NotContain(nameof(InvalidOperationException));
        json.Should().NotContain("   at ", "no stack frames may reach the client");
        json.Should().NotContain("detail", "the detail property must be omitted, not emitted as null");

        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("INTERNAL_ERROR");
        error.GetProperty("message").GetString().Should().Be(UnhandledExceptionResponse.GenericMessage);
        error.GetProperty("traceId").GetString().Should().Be("trace-1");
        error.TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public void BuildErrorBody_InDevelopment_KeepsMessageAndDetail()
    {
        var ex = CaptureThrown();

        var body = UnhandledExceptionResponse.BuildErrorBody(isDevelopment: true, ex, "trace-2");

        body.Error.Code.Should().Be("INTERNAL_ERROR");
        body.Error.Message.Should().Be(ex.Message);
        body.Error.TraceId.Should().Be("trace-2");
        body.Error.Detail.Should().Be(ex.ToString());
        body.Error.Detail.Should().Contain(nameof(InvalidOperationException));
    }

    [Fact]
    public void BuildErrorBody_InDevelopment_SerializesDetailUnderError()
    {
        var ex = CaptureThrown();

        var json = JsonSerializer.Serialize(
            UnhandledExceptionResponse.BuildErrorBody(isDevelopment: true, ex, "trace-3"), WebJson);

        using var doc = JsonDocument.Parse(json);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("message").GetString().Should().Be(LeakyMessage);
        error.GetProperty("detail").GetString().Should().Contain(nameof(InvalidOperationException));
        error.GetProperty("traceId").GetString().Should().Be("trace-3");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildErrorBody_KeepsTheEnvelopeTheFlutterClientParses(bool isDevelopment)
    {
        // lib/core/api/api_client.dart reads data['error']['code'] and data['error']['message'].
        var json = JsonSerializer.Serialize(
            UnhandledExceptionResponse.BuildErrorBody(isDevelopment, CaptureThrown(), "trace-4"), WebJson);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
        error.GetProperty("code").ValueKind.Should().Be(JsonValueKind.String);
        error.GetProperty("message").ValueKind.Should().Be(JsonValueKind.String);
        error.GetProperty("traceId").ValueKind.Should().Be(JsonValueKind.String);
    }

    private static Exception CaptureThrown()
    {
        try
        {
            throw new InvalidOperationException(LeakyMessage);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }
}
