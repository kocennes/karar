using System.IdentityModel.Tokens.Jwt;
using Karar.Api.Services;

namespace Karar.Api.Common.Middleware;

// Banlanan veya hesabını silen kullanıcının elinde kalan access token (15 dk) ile içerik üretmeye
// devam etmesini engeller. Yalnızca durum değiştiren istekler ve geçerli bir kullanıcı JWT'si kontrol
// edilir; okuma istekleri, admin token'ları (sub = e-posta) ve anonim/cihaz istekleri etkilenmez.
public sealed class BannedUserMiddleware(
    RequestDelegate next,
    JwtService jwtService,
    IUserAccessStatusService accessStatus)
{
    // Token veren auth endpoint'leri banı kendisi uygular; cihaz kaydı içerik üretmez.
    private static readonly PathString AuthBasePath = new("/api/v1/auth");
    private static readonly PathString DevicesBasePath = new("/api/v1/devices");
    // Banlı kullanıcı da hesabını silebilmeli (KVKK) ve kaldırılan içeriğe itiraz edebilmeli.
    private static readonly PathString AccountPath = new("/api/v1/users/me");
    private static readonly PathString AppealsPath = new("/api/v1/users/me/moderation-appeals");

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        if (!IsStateChanging(request.Method) || IsExempt(request) || TryGetUserId(request) is not { } userId)
        {
            await next(context);
            return;
        }

        var state = await accessStatus.GetStateAsync(userId, context.RequestAborted);
        if (state == UserAccessState.Banned)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "USER_BANNED",
                    message = "Hesabınız askıya alınmıştır."
                }
            });
            return;
        }

        if (state == UserAccessState.Deleted)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        await next(context);
    }

    private static bool IsStateChanging(string method) =>
        !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) ||
          HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method));

    private static bool IsExempt(HttpRequest request) =>
        request.Path.StartsWithSegments(AuthBasePath, StringComparison.OrdinalIgnoreCase)
        || request.Path.StartsWithSegments(DevicesBasePath, StringComparison.OrdinalIgnoreCase)
        || (HttpMethods.IsDelete(request.Method) && request.Path.Equals(AccountPath, StringComparison.OrdinalIgnoreCase))
        || (HttpMethods.IsPost(request.Method) && request.Path.Equals(AppealsPath, StringComparison.OrdinalIgnoreCase));

    private Guid? TryGetUserId(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var principal = jwtService.ValidateAccessToken(authorization["Bearer ".Length..].Trim());
        // Admin token'larının sub'ı e-postadır; yalnızca kullanıcı (Guid sub) token'ları kontrol edilir.
        var sub = principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
