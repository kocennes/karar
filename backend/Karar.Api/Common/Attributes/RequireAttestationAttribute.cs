namespace Karar.Api.Common.Attributes;

/// Endpoint metadata that opts a route into AppAttestationMiddleware enforcement.
///
/// Usage (Minimal API):
///   app.MapPost("/endpoint", handler).WithMetadata(new RequireAttestationAttribute("endpoint_key"));
///
/// Hard-enforce is controlled per endpoint key via appsettings:
///   "AppAttestation:HardEnforce:endpoint_key": true
///
/// In MVP all endpoints run soft-enforce (signal is recorded, request is never blocked).
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireAttestationAttribute(string endpointKey) : Attribute
{
    public string EndpointKey { get; } = endpointKey;
}
