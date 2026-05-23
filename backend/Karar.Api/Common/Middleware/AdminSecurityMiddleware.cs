using Karar.Api.Data;
using Karar.Api.Services;
using Npgsql;

namespace Karar.Api.Common.Middleware;

// Admin API için ek güvenlik katmanı:
// 1. Tüm admin endpointleri için Redis tabanlı rate limit (login hariç, onun kendi BruteForceService'i var)
// 2. Yetkisiz erişim girişimlerini admin_actions tablosuna yaz
public sealed class AdminSecurityMiddleware
{
    private const int AdminApiLimit = 60;
    private static readonly TimeSpan AdminApiWindow = TimeSpan.FromMinutes(5);
    private static readonly PathString AdminBasePath = new("/api/v1/admin");
    private static readonly PathString AdminAuthPath = new("/api/v1/admin/auth");

    private readonly RequestDelegate _next;
    private readonly RedisService _redis;
    private readonly Db _db;
    private readonly ILogger<AdminSecurityMiddleware> _logger;

    public AdminSecurityMiddleware(
        RequestDelegate next,
        RedisService redis,
        Db db,
        ILogger<AdminSecurityMiddleware> logger)
    {
        _next = next;
        _redis = redis;
        _db = db;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;

        if (!path.StartsWithSegments(AdminBasePath))
        {
            await _next(context);
            return;
        }

        // Phase 5: Production ingress hardening
        // In production, we expect a secret header from Cloud Armor or Internal LB.
        var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (env.IsProduction())
        {
            var config = context.RequestServices.GetRequiredService<IConfiguration>();
            var expectedSecret = config["Admin:IngressSecret"];
            if (!string.IsNullOrEmpty(expectedSecret))
            {
                var providedSecret = context.Request.Headers["X-Admin-Ingress-Secret"].ToString();
                if (providedSecret != expectedSecret)
                {
                    _logger.LogWarning("Admin API: Ingress secret mismatch. IP: {IP}",
                        context.Connection.RemoteIpAddress);
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
            }
        }

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Login endpoint'i BruteForceService + auth-strict ile korunuyor, burada tekrar sınırlama.
        if (!path.StartsWithSegments(AdminAuthPath))
        {
            var allowed = await _redis.IsAllowedAsync("admin-api", ip, AdminApiLimit, AdminApiWindow);
            if (!allowed)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = ((int)AdminApiWindow.TotalSeconds).ToString();
                await context.Response.WriteAsJsonAsync(new
                {
                    error = new
                    {
                        code = "RATE_LIMIT_EXCEEDED",
                        message = "Çok fazla istek. Lütfen bekleyin.",
                        retryAfterSeconds = (int)AdminApiWindow.TotalSeconds
                    }
                });
                return;
            }
        }

        await _next(context);

        // Login endpoint zaten kendi başarısız giriş logu tutuyor; diğer endpointler için 401/403 yaz.
        if (!path.StartsWithSegments(AdminAuthPath) &&
            context.Response.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
        {
            await LogUnauthorizedAccessAsync(ip, context.Request.Method, path, context.Response.StatusCode);
        }
    }

    private async Task LogUnauthorizedAccessAsync(string ip, string method, PathString path, int statusCode)
    {
        try
        {
            await using var connection = await _db.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO admin_actions (admin_email, action, target_type, target_id, note)
                VALUES (@email, @action, @targetType, NULL, @note)
                """,
                connection
            );
            cmd.Parameters.AddWithValue("email", "unknown");
            cmd.Parameters.AddWithValue("action", "unauthorized_access");
            cmd.Parameters.AddWithValue("targetType", "admin");
            cmd.Parameters.AddWithValue("note", $"IP: {ip}, {method} {path}, HTTP {statusCode}");
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin yetkisiz erişim logu yazılamadı: {Method} {Path}", method, path);
        }
    }
}
