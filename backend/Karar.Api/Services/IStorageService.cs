namespace Karar.Api.Services;

public interface IStorageService
{
    bool IsEnabled { get; }

    /// <summary>
    /// Uploads a file to storage and returns the public URL and provider-specific URI.
    /// </summary>
    Task<(string PublicUrl, string StorageUri)?> UploadAsync(
        Stream stream,
        string contentType,
        string objectName,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes an object. Returns true when the object is gone (deleted now or already absent),
    /// false when the provider is disabled or the call failed and should be retried later.
    /// </summary>
    Task<bool> DeleteAsync(string objectName, CancellationToken ct = default);
}
