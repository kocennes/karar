using FluentAssertions;
using Karar.Api.Common;
using Karar.Api.Common.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;

namespace Karar.UnitTests.Security;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void Redact_MasksSensitiveQueryAndJsonValues()
    {
        var input =
            "/api/v1/auth/login?email=ada@example.com&device_id=device-123&otp=123456 {\"refreshToken\":\"abc.def\",\"password\":\"secret\"}";

        var result = SensitiveDataRedactor.Redact(input);

        result.Should().NotContain("ada@example.com");
        result.Should().NotContain("device-123");
        result.Should().NotContain("123456");
        result.Should().NotContain("abc.def");
        result.Should().NotContain("secret");
        result.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void RedactHeader_MasksAuthorizationAndDeviceTokenHeaders()
    {
        SensitiveDataRedactor
            .RedactHeader("Authorization", "Bearer eyJhbGciOiJIUzI1NiJ9.payload")
            .Should()
            .Be("[REDACTED]");

        SensitiveDataRedactor
            .RedactHeader("X-Device-Token", "raw-device-token")
            .Should()
            .Be("[REDACTED]");
    }

    [Fact]
    public void RedactIp_ReducesIpv4ToSubnet()
    {
        var result = SensitiveDataRedactor.RedactIp(IPAddress.Parse("203.0.113.42"));

        result.Should().Be("203.0.113.0/24");
    }
    // ReDoS regression: "a.a.a...@a.a.a..." makes the backtracking email regex quadratic.
    // ~30 KB fits in Kestrel's default 32 KB header limit; before the fix this took seconds of CPU.
    private static string AdversarialEmailLikeInput(int halfPairs) =>
        string.Concat(Enumerable.Repeat("a.", halfPairs)) + "@" + string.Concat(Enumerable.Repeat("a.", halfPairs));

    [Fact]
    public void Redact_AdversarialEmailLikeInput_DoesNotBacktrackCatastrophically()
    {
        var input = AdversarialEmailLikeInput(7_500);
        SensitiveDataRedactor.Redact("warm-up ada@example.com");

        var stopwatch = Stopwatch.StartNew();
        SensitiveDataRedactor.Redact(input);
        SensitiveDataRedactor.RedactHeader("User-Agent", input);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750),
            "redaction runs on every request before ban/rate-limit middleware and must stay linear");
    }

    [Fact]
    public void Redact_StillMasksEmailsAndKeepsIpv4Masking()
    {
        SensitiveDataRedactor.Redact("ua=Mozilla mail ada.lovelace+x@example.com.tr from 203.0.113.42")
            .Should().Be("ua=Mozilla mail [REDACTED] from 203.0.113.0/24");
    }

    [Fact]
    public async Task RequestLoggingMiddleware_HugeUserAgentAndQuery_IsBoundedAndFast()
    {
        var userAgent = AdversarialEmailLikeInput(7_500);
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/anything";
        context.Request.QueryString = new QueryString("?q=" + AdversarialEmailLikeInput(2_000));
        context.Request.Headers.UserAgent = userAgent;
        context.Request.Headers["X-Forwarded-For"] = userAgent;
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.42");

        var logger = new CapturingLogger();
        var middleware = new RedactedRequestLoggingMiddleware(_ => Task.CompletedTask, logger);
        SensitiveDataRedactor.Redact("warm-up ada@example.com");

        var stopwatch = Stopwatch.StartNew();
        await middleware.InvokeAsync(context);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(750));
        logger.Messages.Should().ContainSingle();
        logger.Messages[0].Length.Should().BeLessThan(4_096,
            "logged query string and header values must be truncated before redaction");
        logger.Messages[0].Should().Contain("203.0.113.0/24");
    }

    private sealed class CapturingLogger : ILogger<RedactedRequestLoggingMiddleware>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
