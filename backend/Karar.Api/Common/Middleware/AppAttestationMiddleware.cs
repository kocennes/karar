using Karar.Api.Common.Attributes;
using Karar.Api.Data;
using Karar.Api.Services;

namespace Karar.Api.Common.Middleware;

/// Declarative soft-enforce attestation gate for new endpoints.
///
/// Apply via .WithMetadata(new RequireAttestationAttribute("key")) on the route.
///
/// Existing vote/create_post/report endpoints use inline attestation calls (with
/// transaction context). This middleware is for future routes where inline calls
/// are not needed or where the route should be attested without touching handler code.
///
/// Soft-enforce (default): always continues, records signal to device_trust_scores.
/// Hard-enforce: controlled by AppAttestation:HardEnforce:{endpointKey} config flag.
public sealed class AppAttestationMiddleware(
    RequestDelegate next,
    AppAttestationService attestation,
    Db db,
    ILogger<AppAttestationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, RequestDevice requestDevice)
    {
        var attr = context.GetEndpoint()?.Metadata.GetMetadata<RequireAttestationAttribute>();
        if (attr is null)
        {
            await next(context);
            return;
        }

        var deviceId = await requestDevice.TryGetDeviceIdAsync(context.Request);
        if (deviceId is null)
        {
            await next(context);
            return;
        }

        await using var connection = await db.OpenConnectionAsync();
        var decision = await attestation.VerifyAsync(
            context.Request, connection, null, deviceId.Value, attr.EndpointKey);

        if (decision.ShouldBlock)
        {
            logger.LogWarning(
                "AppAttestationMiddleware blocked request. Endpoint={EndpointKey} DeviceId={DeviceId} Provider={Provider} Status={Status}",
                attr.EndpointKey, deviceId.Value, decision.Provider, decision.Status);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new
                {
                    code = "APP_ATTESTATION_FAILED",
                    message = "Uygulama doğrulaması başarısız."
                }
            });
            return;
        }

        await next(context);
    }
}
