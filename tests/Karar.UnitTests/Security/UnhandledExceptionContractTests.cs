using System.Text.RegularExpressions;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Security;

/// Source contract tests for the global exception middleware in Program.cs: outside
/// Development a client must only receive a generic message plus a traceId. ex.Message
/// (Npgsql connection strings, internal paths) and ex.ToString() (stack traces) belong in
/// the log line, never in the response body.
public sealed class UnhandledExceptionContractTests
{
    [Fact]
    public void GlobalExceptionMiddleware_DoesNotWriteExceptionDetailsInline()
    {
        var block = SliceGlobalExceptionHandlerBlock();

        block.Should().NotContain("ex.ToString()",
            "stack traces must never be written to the response; only UnhandledExceptionResponse decides that, and only in Development");
        block.Should().NotContain("ex.Message",
            "exception messages can carry connection strings and internal paths");
        block.Should().NotContain("detail = ");
    }

    [Fact]
    public void GlobalExceptionMiddleware_IncludesTraceIdAndIsEnvironmentAware()
    {
        var block = SliceGlobalExceptionHandlerBlock();

        block.Should().Contain("traceId");
        block.Should().Contain("System.Diagnostics.Activity.Current?.Id ?? ctx.TraceIdentifier");
        block.Should().Contain("UnhandledExceptionResponse.BuildErrorBody(app.Environment.IsDevelopment(), ex, traceId)");
        block.Should().Contain("logger.LogError(ex,");
        block.Should().Contain("{TraceId}", "the log line must carry the traceId so support can correlate it with the client's error");
    }

    [Fact]
    public void Program_NeverPutsExceptionTextIntoResponses()
    {
        var program = ReadProgram();

        var matches = Regex.Matches(program, @"\b(ex|e|exception)\.(Message\b|ToString\(\)|StackTrace\b)");

        matches.Select(m => m.Value).Should().BeEmpty(
            "exception text belongs in logs (logger.LogError(ex, ...)), not in response bodies");
    }

    private static string ReadProgram() =>
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    private static string SliceGlobalExceptionHandlerBlock()
    {
        var text = ReadProgram();
        const string startMarker = "// Global exception handler";

        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the global exception handler middleware should exist");

        var catchIndex = text.IndexOf("catch (Exception ex)", start, StringComparison.Ordinal);
        catchIndex.Should().BeGreaterThan(start, "the middleware should catch every exception");

        var end = text.IndexOf("\n});", catchIndex, StringComparison.Ordinal);
        end.Should().BeGreaterThan(catchIndex, "the middleware lambda should be closed with '});'");

        return text[start..(end + "\n});".Length)];
    }
}
