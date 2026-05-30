using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Karar.Api.Observability;

namespace Karar.Api.Services;

/// FCM sender that bootstraps Firebase Admin SDK independently.
/// Constructed as a singleton at startup — Firebase is guaranteed to be
/// initialized before NotificationDispatcher starts its first cycle.
public sealed class FirebaseFcmSender : IFcmSender
{
    public bool IsAvailable { get; }

    public FirebaseFcmSender(IConfiguration configuration, ILogger<FirebaseFcmSender> logger)
    {
        if (FirebaseApp.DefaultInstance is not null)
        {
            IsAvailable = true;
            return;
        }

        var credJson = configuration["Firebase:ServiceAccountJson"];
        try
        {
            var credential = string.IsNullOrEmpty(credJson)
                ? GoogleCredential.GetApplicationDefault()
                : GoogleCredential.FromJson(credJson);

            FirebaseApp.Create(new AppOptions { Credential = credential });
            IsAvailable = true;
            logger.LogInformation("Firebase Admin SDK initialized — push notifications enabled");
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            logger.LogError(ex,
                "Firebase Admin SDK initialization failed — push notifications are unavailable. " +
                "Verify Firebase:ServiceAccountJson config or Application Default Credentials (ADC).");
        }
    }

    public async Task<string> SendAsync(Message message, CancellationToken ct)
    {
        using var activity = KararTelemetry.StartActivity("fcm.send", System.Diagnostics.ActivityKind.Client);
        activity?.SetTag("messaging.system", "firebase_cloud_messaging");
        activity?.SetTag("messaging.operation.name", "send");
        activity?.SetTag("karar.notification_type",
            message.Data is not null && message.Data.TryGetValue("type", out var type) ? type : "unknown");

        return await FirebaseMessaging.DefaultInstance.SendAsync(message, ct);
    }
}
