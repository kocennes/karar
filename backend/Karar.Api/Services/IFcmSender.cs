using FirebaseAdmin.Messaging;

namespace Karar.Api.Services;

/// Push delivery abstraction over Firebase Cloud Messaging.
/// Implementations must initialize their own Firebase Admin SDK instance —
/// the dispatcher must not rely on FirebaseAuthService as a side effect.
public interface IFcmSender
{
    /// True when the Firebase Admin SDK was successfully initialized.
    bool IsAvailable { get; }

    Task<string> SendAsync(Message message, CancellationToken ct);
}
