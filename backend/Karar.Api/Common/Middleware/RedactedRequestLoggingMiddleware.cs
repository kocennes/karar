using System.Diagnostics;
using Karar.Api.Common;

namespace Karar.Api.Common.Middleware;

public sealed class RedactedRequestLoggingMiddleware
{
    private static readonly HashSet<string> LoggedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "X-Device-Token",
        "X-Forwarded-For",
        "User-Agent"
    };

    private readonly RequestDelegate _next;
    private readonly ILogger<RedactedRequestLoggingMiddleware> _logger;

    public RedactedRequestLoggingMiddleware(
        RequestDelegate next,
        ILogger<RedactedRequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogInformation(
                "HTTP {Method} {Path}{Query} responded {StatusCode} in {ElapsedMs}ms from {ClientIp} headers {Headers}",
                context.Request.Method,
                context.Request.Path.Value,
                SensitiveDataRedactor.Redact(context.Request.QueryString.Value),
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                SensitiveDataRedactor.RedactIp(context.Connection.RemoteIpAddress),
                BuildHeaderSummary(context.Request.Headers));
        }
    }

    private static string BuildHeaderSummary(IHeaderDictionary headers)
    {
        var logged = headers
            .Where(header => LoggedHeaders.Contains(header.Key))
            .Select(header =>
                $"{header.Key}={SensitiveDataRedactor.RedactHeader(header.Key, header.Value.ToString())}");

        return string.Join("; ", logged);
    }
}
