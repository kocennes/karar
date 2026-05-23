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
}
