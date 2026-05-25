using Karar.Api.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace Karar.Api.Common.Middleware;

public sealed class DistributedRateLimitMiddleware(RequestDelegate next, RedisService redis)
{
    public async Task InvokeAsync(HttpContext context, RequestDevice requestDevice)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is null)
        {
            await next(context);
            return;
        }

        var metadata = endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>();
        if (metadata is null)
        {
            await next(context);
            return;
        }

        var policyName = metadata.PolicyName ?? string.Empty;
        var (limit, window) = GetPolicyConfig(policyName);

        if (limit > 0)
        {
            var deviceId = await requestDevice.TryGetDeviceIdAsync(context.Request);
            var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

            // Composite key: IP + DeviceId (if available) to prevent spoofing bypass
            var identity = deviceId != null
                ? $"{clientIp}:{deviceId}"
                : clientIp;

            var isAllowed = await redis.IsAllowedAsync(policyName, identity, limit, window);

            if (!isAllowed)
            {
                var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling(window.TotalSeconds));
                context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new
                    {
                        code = "RATE_LIMIT_EXCEEDED",
                        message = "Çok fazla istek. Lütfen bekleyin.",
                        retryAfterSeconds
                    }
                });
                return;
            }
        }

        await next(context);
    }

    private (int limit, TimeSpan window) GetPolicyConfig(string policyName)
    {
        return policyName switch
        {
            "post-create" => (5, TimeSpan.FromHours(1)),
            "report-create" => (10, TimeSpan.FromHours(1)),
            "auth-strict" => (10, TimeSpan.FromMinutes(15)),
            _ => (0, TimeSpan.Zero)
        };
    }
}
