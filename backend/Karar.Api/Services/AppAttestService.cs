namespace Karar.Api.Services;

/// App Attest verification stub for iOS.
///
/// Enable via config: iOS:AppAttestEnabled = true
/// Implementation path when enabled:
///   1. Client calls GET /api/v1/devices/nonce → receives nonce
///   2. Client generates App Attest assertion via DCAppAttestService
///   3. Client sends assertion + key-id in POST /api/v1/devices/register
///   4. Backend verifies against Apple's App Attest API
///
/// Currently returns Skipped — iOS:AppAttestEnabled not yet set in production.
/// Soft-enforce: disabled provider never fails a request.
public sealed class AppAttestService(IConfiguration config) : IIntegrityProvider
{
    public string ProviderName => "app-attest";
    public bool IsEnabled => config.GetValue<bool>("iOS:AppAttestEnabled");

    public Task<IntegrityTokenStatus> VerifyAsync(string token, string nonce) =>
        Task.FromResult(IntegrityTokenStatus.Skipped);
}
