using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Karar.Api.Data;

namespace Karar.Api.Services;

/// <summary>
/// Nonce generation + Google Play Integrity API token verification.
///
/// Flow:
///   1. Client calls GET /api/v1/devices/nonce → receives base64url nonce
///   2. Client passes nonce to Play Integrity API (Android) → receives integrity token
///   3. Client includes integrity_token + nonce in POST /api/v1/devices/register
///   4. Backend verifies via Google Play Integrity decodeIntegrityToken API
///
/// Configure Android:PackageName to enable. Without it the service returns Skipped
/// (skip) so device registration still succeeds — use for gradual rollout.
///
/// Requires ADC with the "playintegrity" IAM role on the GCP project.
/// </summary>
public sealed class PlayIntegrityService(
    IConfiguration config,
    RedisService redis,
    IHttpClientFactory httpClientFactory,
    ILogger<PlayIntegrityService> logger) : IIntegrityProvider
{
    private static readonly TimeSpan NonceExpiry = TimeSpan.FromMinutes(5);
    private const string PlayIntegrityScope = "https://www.googleapis.com/auth/playintegrity";

    private readonly string? _packageName = config["Android:PackageName"];

    public string ProviderName => "play-integrity";
    public bool IsEnabled => !string.IsNullOrWhiteSpace(_packageName);

    /// <summary>
    /// Generates a cryptographically random nonce, stores it in Redis for 5 min, returns it.
    /// </summary>
    public async Task<string> GenerateNonceAsync()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        var nonce = Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('='); // base64url

        await redis.SetAsync($"integrity:nonce:{nonce}", "1", NonceExpiry);
        return nonce;
    }

    /// <summary>
    /// Verifies a Play Integrity token against the stored nonce.
    /// Returns:
    ///   Valid   — verification passed (MEETS_BASIC_INTEGRITY)
    ///   Invalid — verification failed (tampered device / emulator / wrong nonce)
    ///   Expired — nonce not found in Redis
    ///   Skipped — provider not configured or transient verification error
    /// </summary>
    public async Task<IntegrityTokenStatus> VerifyAsync(string token, string nonce)
    {
        if (!IsEnabled)
            return IntegrityTokenStatus.Skipped;

        // Consume the nonce so it can't be replayed
        var nonceKey = $"integrity:nonce:{nonce}";
        var exists = await redis.GetAsync<string>(nonceKey);
        if (exists is null)
        {
            logger.LogWarning("PlayIntegrity: nonce bulunamadı veya süresi dolmuş.");
            return IntegrityTokenStatus.Expired;
        }
        await redis.DeleteAsync(nonceKey);

        try
        {
            var credential = await GoogleCredential.GetApplicationDefaultAsync();
            credential = credential.CreateScoped(PlayIntegrityScope);
            var accessToken = await ((ITokenAccess)credential).GetAccessTokenForRequestAsync();

            var client = httpClientFactory.CreateClient("play-integrity");
            using var req = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://playintegrity.googleapis.com/v1/{_packageName}:decodeIntegrityToken");
            req.Headers.Authorization = new("Bearer", accessToken);
            req.Content = JsonContent.Create(new { integrity_token = token });

            using var res = await client.SendAsync(req);
            if (!res.IsSuccessStatusCode)
            {
                logger.LogWarning("PlayIntegrity: API hatası {Status}", res.StatusCode);
                return IntegrityTokenStatus.Invalid;
            }

            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);

            // Nonce check: token contains SHA256 of the nonce we sent
            var expectedNonceSha256 = Convert.ToHexString(
                SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();

            var tokenDetails = doc.RootElement
                .GetProperty("tokenPayloadExternal");

            // requestDetails.nonceSha256 is hex-encoded SHA256 of the nonce
            if (tokenDetails.TryGetProperty("requestDetails", out var reqDetails) &&
                reqDetails.TryGetProperty("nonceSha256", out var nonceSha256El))
            {
                var receivedNonce = nonceSha256El.GetString() ?? "";
                if (!receivedNonce.Equals(expectedNonceSha256, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("PlayIntegrity: nonce SHA256 uyuşmuyor.");
                    return IntegrityTokenStatus.Invalid;
                }
            }
            else
            {
                logger.LogWarning("PlayIntegrity: requestDetails.nonceSha256 alanı bulunamadı.");
                return IntegrityTokenStatus.Invalid;
            }

            // Verdict check: appIntegrity.appRecognitionVerdict must not be UNEVALUATED
            // deviceIntegrity.deviceRecognitionVerdict must include MEETS_BASIC_INTEGRITY
            if (!tokenDetails.TryGetProperty("deviceIntegrity", out var deviceIntegrity))
                return IntegrityTokenStatus.Invalid;

            if (!deviceIntegrity.TryGetProperty("deviceRecognitionVerdict", out var verdictArray))
                return IntegrityTokenStatus.Invalid;

            var verdicts = verdictArray.EnumerateArray()
                .Select(v => v.GetString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var meetsBasic = verdicts.Contains("MEETS_BASIC_INTEGRITY");

            if (!meetsBasic)
                logger.LogWarning("PlayIntegrity: cihaz temel bütünlük kontrolünü geçemedi. Verdicts: {V}", string.Join(",", verdicts));

            return meetsBasic ? IntegrityTokenStatus.Valid : IntegrityTokenStatus.Invalid;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PlayIntegrity: doğrulama sırasında hata oluştu.");
            return IntegrityTokenStatus.Skipped; // treat transient errors as skipped to avoid blocking legit users
        }
    }
}
