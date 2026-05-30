using System.Text.Json;
using Npgsql;

namespace Karar.Api.Services;

public sealed class AppAttestationService(
    IConfiguration configuration,
    PlayIntegrityService playIntegrity,
    AppAttestService appAttest,
    FirebaseAppCheckService appCheck,
    DeviceTrustService deviceTrust,
    ILogger<AppAttestationService> logger)
{
    public async Task<AppAttestationDecision> VerifyAsync(
        HttpRequest request,
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid deviceId,
        string endpointKey
    )
    {
        var platform = await GetPlatformAsync(connection, transaction, deviceId);
        var result = await VerifyHeadersAsync(request, platform);
        var hardEnforce = IsHardEnforced(endpointKey);

        await deviceTrust.RecordAttestationSignalAsync(connection, transaction, deviceId, result.Status);
        await LogSecurityEventAsync(connection, transaction, deviceId, endpointKey, platform, result, hardEnforce);

        var shouldBlock = hardEnforce && result.Status is IntegrityTokenStatus.Missing or IntegrityTokenStatus.Invalid or IntegrityTokenStatus.Expired;
        if (shouldBlock)
        {
            logger.LogWarning(
                "App attestation hard-enforce blocked request. Endpoint={Endpoint} DeviceId={DeviceId} Platform={Platform} Provider={Provider} Status={Status}",
                endpointKey,
                deviceId,
                platform,
                result.Provider,
                result.Status);
        }
        else if (result.Status is not (IntegrityTokenStatus.Valid or IntegrityTokenStatus.Skipped))
        {
            logger.LogWarning(
                "App attestation soft-enforce signal. Endpoint={Endpoint} DeviceId={DeviceId} Platform={Platform} Provider={Provider} Status={Status}",
                endpointKey,
                deviceId,
                platform,
                result.Provider,
                result.Status);
        }

        return new AppAttestationDecision(result.Provider, result.Status, hardEnforce, shouldBlock);
    }

    public async Task<AppAttestationDecision> VerifyDeviceRegistrationAsync(
        HttpRequest request,
        NpgsqlConnection connection,
        Guid deviceId,
        string platform,
        string? integrityToken,
        string? nonce
    )
    {
        var result = await VerifyHeadersAsync(request, platform, integrityToken, nonce);
        var endpointKey = "device_register";
        var hardEnforce = IsHardEnforced(endpointKey);

        await deviceTrust.RecordAttestationSignalAsync(connection, null, deviceId, result.Status);
        await LogSecurityEventAsync(connection, null, deviceId, endpointKey, platform, result, hardEnforce);

        return new AppAttestationDecision(
            result.Provider,
            result.Status,
            hardEnforce,
            hardEnforce && result.Status is IntegrityTokenStatus.Missing or IntegrityTokenStatus.Invalid or IntegrityTokenStatus.Expired);
    }

    public static IntegrityTokenStatus NormalizeProviderStatus(bool providerEnabled, string? token, string? nonce, IntegrityTokenStatus providerStatus)
    {
        if (!providerEnabled)
            return IntegrityTokenStatus.Skipped;

        if (string.IsNullOrWhiteSpace(token))
            return IntegrityTokenStatus.Missing;

        if (string.IsNullOrWhiteSpace(nonce))
            return IntegrityTokenStatus.Missing;

        return providerStatus;
    }

    private async Task<AppAttestationProviderResult> VerifyHeadersAsync(
        HttpRequest request,
        string platform,
        string? fallbackPlatformToken = null,
        string? fallbackNonce = null
    )
    {
        var appCheckToken = Header(request, "X-Firebase-AppCheck");
        if (!string.IsNullOrWhiteSpace(appCheckToken) || appCheck.IsEnabled)
        {
            var appCheckStatus = NormalizeProviderStatus(
                appCheck.IsEnabled,
                appCheckToken,
                "app-check",
                string.IsNullOrWhiteSpace(appCheckToken)
                    ? IntegrityTokenStatus.Missing
                    : await appCheck.VerifyAsync(appCheckToken, "app-check"));

            if (appCheckStatus != IntegrityTokenStatus.Skipped)
                return new AppAttestationProviderResult(appCheck.ProviderName, appCheckStatus);
        }

        if (platform.Equals("android", StringComparison.OrdinalIgnoreCase))
        {
            var token = Header(request, "X-Play-Integrity-Token");
            var nonce = Header(request, "X-Integrity-Nonce");
            token = string.IsNullOrWhiteSpace(token) ? fallbackPlatformToken ?? "" : token;
            nonce = string.IsNullOrWhiteSpace(nonce) ? fallbackNonce ?? "" : nonce;
            var providerStatus = string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(nonce)
                ? IntegrityTokenStatus.Missing
                : await playIntegrity.VerifyAsync(token, nonce);

            return new AppAttestationProviderResult(
                playIntegrity.ProviderName,
                NormalizeProviderStatus(playIntegrity.IsEnabled, token, nonce, providerStatus));
        }

        if (platform.Equals("ios", StringComparison.OrdinalIgnoreCase))
        {
            var token = Header(request, "X-App-Attest-Token");
            var nonce = Header(request, "X-Integrity-Nonce");
            token = string.IsNullOrWhiteSpace(token) ? fallbackPlatformToken ?? "" : token;
            nonce = string.IsNullOrWhiteSpace(nonce) ? fallbackNonce ?? "" : nonce;
            var providerStatus = string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(nonce)
                ? IntegrityTokenStatus.Missing
                : await appAttest.VerifyAsync(token, nonce);

            return new AppAttestationProviderResult(
                appAttest.ProviderName,
                NormalizeProviderStatus(appAttest.IsEnabled, token, nonce, providerStatus));
        }

        return new AppAttestationProviderResult("unknown", IntegrityTokenStatus.Skipped);
    }

    private bool IsHardEnforced(string endpointKey) =>
        configuration.GetValue<bool>($"AppAttestation:HardEnforce:{endpointKey}");

    private static string Header(HttpRequest request, string name) =>
        request.Headers.TryGetValue(name, out var value) ? value.ToString() : "";

    private static async Task<string> GetPlatformAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid deviceId
    )
    {
        await using var command = new NpgsqlCommand("SELECT platform FROM devices WHERE id = @deviceId", connection, transaction);
        command.Parameters.AddWithValue("deviceId", deviceId);
        return (string?)await command.ExecuteScalarAsync() ?? "unknown";
    }

    private static async Task LogSecurityEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        Guid deviceId,
        string endpointKey,
        string platform,
        AppAttestationProviderResult result,
        bool hardEnforce
    )
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO security_events (
                event_type,
                device_id,
                endpoint_key,
                platform,
                provider,
                token_status,
                enforce_mode,
                metadata
            )
            VALUES (
                @eventType,
                @deviceId,
                @endpointKey,
                @platform,
                @provider,
                @tokenStatus,
                @enforceMode,
                @metadata::jsonb
            )
            """,
            connection,
            transaction
        );
        command.Parameters.AddWithValue("eventType", "attestation_checked");
        command.Parameters.AddWithValue("deviceId", deviceId);
        command.Parameters.AddWithValue("endpointKey", endpointKey);
        command.Parameters.AddWithValue("platform", platform);
        command.Parameters.AddWithValue("provider", result.Provider);
        command.Parameters.AddWithValue("tokenStatus", result.Status.ToString().ToLowerInvariant());
        command.Parameters.AddWithValue("enforceMode", hardEnforce ? "hard" : "soft");
        command.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(new
        {
            falsePositiveMeasurement = true,
            wouldBlock = result.Status is IntegrityTokenStatus.Missing or IntegrityTokenStatus.Invalid or IntegrityTokenStatus.Expired
        }));
        await command.ExecuteNonQueryAsync();
    }
}

public sealed record AppAttestationProviderResult(string Provider, IntegrityTokenStatus Status);

public sealed record AppAttestationDecision(
    string Provider,
    IntegrityTokenStatus Status,
    bool HardEnforce,
    bool ShouldBlock
);
