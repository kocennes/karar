using FirebaseAdmin;
using FirebaseAdmin.Auth;
using Google.Apis.Auth.OAuth2;

namespace Karar.Api.Services;

// Not sealed, VerifyIdTokenAsync virtual: the integration tests substitute a fake verifier
// (a real Google ID token cannot be minted in a test).
public class FirebaseAuthService
{
    private readonly ILogger<FirebaseAuthService>? _logger;

    public FirebaseAuthService(IConfiguration configuration, ILogger<FirebaseAuthService> logger)
    {
        _logger = logger;
        if (FirebaseApp.DefaultInstance is not null) return;

        var credentialJson = configuration["Firebase:ServiceAccountJson"];
        try
        {
            var credential = string.IsNullOrEmpty(credentialJson)
                ? GoogleCredential.GetApplicationDefault()
                : GoogleCredential.FromJson(credentialJson);

            FirebaseApp.Create(new AppOptions { Credential = credential });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firebase başlatılamadı. Google Sign-In devre dışı.");
            // Uygulama yine başlar; Google Sign-In endpoint'i 400 döner.
        }
    }

    public virtual async Task<FirebaseToken?> VerifyIdTokenAsync(string idToken)
    {
        if (FirebaseApp.DefaultInstance is null) return null;
        try
        {
            return await FirebaseAuth.DefaultInstance.VerifyIdTokenAsync(idToken);
        }
        catch
        {
            return null;
        }
    }
}
