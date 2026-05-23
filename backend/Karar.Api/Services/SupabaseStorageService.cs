using System.Net.Http.Headers;

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
}
