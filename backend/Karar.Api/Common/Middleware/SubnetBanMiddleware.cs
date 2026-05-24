using Karar.Api.Services;

namespace Karar.Api.Common.Middleware;

public sealed class SubnetBanMiddleware(RequestDelegate next, SubnetBanService subnetBan)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (await subnetBan.IsBannedAsync(ip))
        {
            context.Response.Headers.RetryAfter = ((int)SubnetBanService.AutoBanDuration.TotalSeconds).ToString();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "SUBNET_RATE_LIMIT",
                    message = "Bu ağdan çok fazla istek geldi. Lütfen bir süre sonra tekrar deneyin.",
                    retryAfterSeconds = (int)SubnetBanService.AutoBanDuration.TotalSeconds
                }
            });
            return;
        }

        await next(context);

        if (context.Response.StatusCode == StatusCodes.Status429TooManyRequests)
        {
            await subnetBan.RecordRateLimitRejectionAsync(ip);
        }
        else
        {
            await subnetBan.ClearRateLimitRejectionsAsync(ip);
        }
    }
}
