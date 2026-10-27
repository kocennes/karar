using Google.Cloud.Storage.V1;

namespace Karar.Api.Services;

/// <summary>
/// Uploads images to Google Cloud Storage and returns the public URL.
/// Uses Application Default Credentials — works automatically on Cloud Run.
/// Locally: set GOOGLE_APPLICATION_CREDENTIALS env var.
/// </summary>
public sealed class CloudStorageService(IConfiguration config, ILogger<CloudStorageService> logger) : IStorageService
{
    private readonly string _bucketName = config["GCS:BucketName"] ?? "karar-images";
    private readonly string _cdnBase = config["GCS:CdnBase"] ?? "";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(config["GCS:BucketName"]);

    public async Task<(string PublicUrl, string StorageUri)?> UploadAsync(
        Stream stream,
        string contentType,
        string objectName,
        CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            logger.LogWarning("GCS upload skipped — GCS:BucketName not configured.");
            return null;
        }

        try
        {
            var client = await StorageClient.CreateAsync();
            var obj = new Google.Apis.Storage.v1.Data.Object
            {
                Bucket = _bucketName,
                Name = objectName,
                ContentType = contentType,
                // Kısa önbellek: silinen/kaldırılan görsel CDN ve tarayıcılarda en geç 1 saatte düşer.
                CacheControl = "public, max-age=3600",
            };
            await client.UploadObjectAsync(obj, stream, cancellationToken: ct);

            var storageUri = $"gs://{_bucketName}/{objectName}";
            var publicUrl = string.IsNullOrWhiteSpace(_cdnBase)
                ? $"https://storage.googleapis.com/{_bucketName}/{objectName}"
                : $"{_cdnBase.TrimEnd('/')}/{objectName}";

            logger.LogInformation("Uploaded {Object} to GCS", objectName);
            return (publicUrl, storageUri);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GCS upload failed for {Object}", objectName);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string objectName, CancellationToken ct = default)
    {
        if (!IsEnabled) return false;

        try
        {
            var client = await StorageClient.CreateAsync();
            await client.DeleteObjectAsync(_bucketName, objectName, cancellationToken: ct);
            logger.LogInformation("Deleted {Object} from GCS", objectName);
            return true;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "GCS delete failed for {Object}", objectName);
            return false;
        }
    }
}
