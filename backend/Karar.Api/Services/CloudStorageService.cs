using Google.Cloud.Storage.V1;

namespace Karar.Api.Services;

/// <summary>
/// Uploads images to Google Cloud Storage and returns the public URL.
/// Uses Application Default Credentials — works automatically on Cloud Run.
/// Locally: set GOOGLE_APPLICATION_CREDENTIALS env var.
/// </summary>
public sealed class CloudStorageService(IConfiguration config, ILogger<CloudStorageService> logger)
{
    private readonly string _bucketName = config["GCS:BucketName"] ?? "karar-images";
    private readonly string _cdnBase = config["GCS:CdnBase"] ?? "";

    public bool IsEnabled => !string.IsNullOrWhiteSpace(config["GCS:BucketName"]);

    /// <summary>
    /// Uploads <paramref name="stream"/> to GCS under <paramref name="objectName"/>.
    /// Returns (publicHttpUrl, gsUri) on success, null on failure.
    /// </summary>
    public async Task<(string PublicUrl, string GcsUri)?> UploadAsync(
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
                CacheControl = "public, max-age=31536000, immutable",
            };
            await client.UploadObjectAsync(obj, stream, cancellationToken: ct);

            var gcsUri = $"gs://{_bucketName}/{objectName}";
            var publicUrl = string.IsNullOrWhiteSpace(_cdnBase)
                ? $"https://storage.googleapis.com/{_bucketName}/{objectName}"
                : $"{_cdnBase.TrimEnd('/')}/{objectName}";

            logger.LogInformation("Uploaded {Object} to GCS", objectName);
            return (publicUrl, gcsUri);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "GCS upload failed for {Object}", objectName);
            return null;
        }
    }
}
