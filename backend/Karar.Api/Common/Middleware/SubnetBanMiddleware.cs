using Karar.Api.Services;

namespace Karar.Api.Common.Middleware;

public sealed class SubnetBanMiddleware(RequestDelegate next, SubnetBanService subnetBan)
{
    private static readonly PathString HealthPrefix = new("/health");

    /// Sağlık/uptime ping'leri ban kontrolünden muaf: Redis ban önbelleği 1 dk yaşar, keepalive 5-10 dk'da
    /// bir gelir; her ping önbelleği ıskalayıp banned_subnets'i Postgres'te sorgular ve Neon'u uyandırırdı.
    /// Yasaklı bir ağın /health'e ulaşabilmesi zararsızdır (DB'siz self check).
    public static bool SkipsBanCheck(PathString path) => path.StartsWithSegments(HealthPrefix);

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

        // Yalnızca rate limiter retleri sayılır; endpoint kotası 429'ları ne sayılır ne sıfırlar.
        if (SubnetBanService.IsLimiterRejection(context))
        {
            await subnetBan.RecordRateLimitRejectionAsync(ip);
        }
        else if (context.Response.StatusCode != StatusCodes.Status429TooManyRequests)
        {
            await subnetBan.ClearRateLimitRejectionsAsync(ip);
        }
    }
}
