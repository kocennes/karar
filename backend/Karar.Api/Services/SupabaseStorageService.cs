using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace Karar.Api.Services;

public sealed class SupabaseStorageService(
    IConfiguration config,
    ILogger<SupabaseStorageService> logger,
    IHttpClientFactory httpClientFactory) : IStorageService
{
    private readonly string _url = config["Supabase:Url"] ?? "";
    private readonly string _key = config["Supabase:Key"] ?? "";
    private readonly string _bucket = config["Supabase:Bucket"] ?? "karar-images";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_url) && !string.IsNullOrWhiteSpace(_key);

    public async Task<(string PublicUrl, string StorageUri)?> UploadAsync(
        Stream stream,
        string contentType,
        string objectName,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            logger.LogWarning("Supabase upload skipped — Config missing.");
            return null;
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            client.DefaultRequestHeaders.Add("apiKey", _key);

            // Supabase path format: /storage/v1/object/[bucket]/[path]
            var uploadUrl = $"{_url.TrimEnd('/')}/storage/v1/object/{_bucket}/{objectName}";

            using var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

            var response = await client.PostAsync(uploadUrl, content, ct);
            response.EnsureSuccessStatusCode();

            // Public URL: [url]/storage/v1/object/public/[bucket]/[path]
            var publicUrl = $"{_url.TrimEnd('/')}/storage/v1/object/public/{_bucket}/{objectName}";
            var storageUri = $"supabase://{_bucket}/{objectName}";

            logger.LogInformation("Uploaded {Object} to Supabase", objectName);
            return (publicUrl, storageUri);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Supabase upload failed for {Object}", objectName);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string objectName, CancellationToken ct = default)
    {
        if (!IsEnabled) return false;

        try
        {
            var client = httpClientFactory.CreateClient();

            // Supabase bulk remove: DELETE /storage/v1/object/[bucket] {"prefixes": [path]}; absent paths are a no-op.
            using var request = new HttpRequestMessage(
                HttpMethod.Delete,
                $"{_url.TrimEnd('/')}/storage/v1/object/{_bucket}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            request.Headers.Add("apiKey", _key);
            request.Content = JsonContent.Create(new { prefixes = new[] { objectName } });

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("Supabase delete failed for {Object} with {Status}", objectName, (int)response.StatusCode);
                return false;
            }

            logger.LogInformation("Deleted {Object} from Supabase", objectName);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Supabase delete failed for {Object}", objectName);
            return false;
        }
    }
}
