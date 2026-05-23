using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace Karar.Api.Services;

public sealed class ImageProcessorService(ILogger<ImageProcessorService> logger)
{
    private const int MaxDimension = 1200;

    /// <summary>
    /// Processes an image: strips metadata (EXIF), resizes if too large, and converts to optimized JPEG.
    /// </summary>
    public async Task<MemoryStream?> ProcessAsync(Stream inputStream, CancellationToken ct = default)
    {
        try
        {
            var outputStream = new MemoryStream();
            using var image = await Image.LoadAsync(inputStream, ct);

            // Strip metadata (EXIF data like GPS coordinates)
            image.Metadata.ExifProfile = null;
            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;

            // Resize if exceeds max dimensions
            if (image.Width > MaxDimension || image.Height > MaxDimension)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Size = new Size(MaxDimension, MaxDimension),
                    Mode = ResizeMode.Max
                }));
            }

            // Encode as optimized Jpeg
            await image.SaveAsJpegAsync(outputStream, new JpegEncoder
            {
                Quality = 75
            }, ct);

            outputStream.Position = 0;
            return outputStream;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image processing failed");
            return null;
        }
    }
}
