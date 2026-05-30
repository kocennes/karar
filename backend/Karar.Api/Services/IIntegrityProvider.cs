namespace Karar.Api.Services;

/// Unified interface for platform attestation providers.
///
/// Providers:
///   Android  → PlayIntegrityService  (config: Android:PackageName)
///   iOS      → AppAttestService      (config: iOS:AppAttestEnabled)
///   Web/All  → FirebaseAppCheckService (config: Firebase:AppCheckEnabled)
///
/// Return semantics:
///   Valid   — attestation passed (device is genuine)
///   Invalid — attestation failed (tampered / emulator / wrong nonce)
///   Expired — token/nonce was recognized as expired or already consumed
///   Skipped — provider not configured or transient error
///
/// Skipped means "no signal available" — never treat as failure. Enforcement
/// is always soft: missing attestation increases risk score but does not
/// block the request (MVP soft-enforce mode).
public interface IIntegrityProvider
{
    string ProviderName { get; }
    bool IsEnabled { get; }
    Task<IntegrityTokenStatus> VerifyAsync(string token, string nonce);
}

public enum IntegrityTokenStatus
{
    Valid,
    Missing,
    Invalid,
    Expired,
    Skipped
}
