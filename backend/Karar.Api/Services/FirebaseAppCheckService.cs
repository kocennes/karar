namespace Karar.Api.Services;

/// Firebase App Check stub for web and cross-platform protection.
///
/// Enable via config: Firebase:AppCheckEnabled = true
/// Implementation path when enabled:
///   1. Client initialises Firebase App Check SDK (reCAPTCHA / DeviceCheck)
///   2. Client obtains App Check token and sends it in X-Firebase-AppCheck header
///   3. Backend verifies via Firebase App Check REST API (verifyToken)
///
/// Currently returns Skipped — Firebase:AppCheckEnabled not yet set.
/// Soft-enforce: disabled provider never fails a request.
public sealed class FirebaseAppCheckService(IConfiguration config) : IIntegrityProvider
{
    public string ProviderName => "firebase-app-check";
    public bool IsEnabled => config.GetValue<bool>("Firebase:AppCheckEnabled");

    public Task<IntegrityTokenStatus> VerifyAsync(string token, string nonce) =>
        Task.FromResult(IntegrityTokenStatus.Skipped);
}
