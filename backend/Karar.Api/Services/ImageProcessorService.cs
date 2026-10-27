using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Memory;
using SixLabors.ImageSharp.Processing;

namespace Karar.Api.Services;

public sealed class ImageProcessorService(ILogger<ImageProcessorService> logger)
{
    private const int MaxDimension = 1200;

    // Decompression-bomb guards: a few-MB upload can declare a 30000x30000 canvas or hundreds of
    // animation frames. Dimensions are read from the header (Identify) before any pixel buffer is
    // allocated; only the first frame is decoded and each buffer is capped by the allocator.
    internal const int MaxInputDimension = 8000;
    internal const long MaxInputPixels = 40_000_000;
    private const int MaxConcurrentDecodes = 2;
    private static readonly TimeSpan DecodeQueueTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DecodeTimeout = TimeSpan.FromSeconds(10);
    private static readonly SemaphoreSlim DecodeGate = new(MaxConcurrentDecodes, MaxConcurrentDecodes);

    // Only the formats ImageValidator accepts (no GIF/TIFF/BMP/TGA/PBM/QOI decoders).
    private static readonly Configuration DecodeConfiguration = new(
        new JpegConfigurationModule(),
        new PngConfigurationModule(),
        new WebpConfigurationModule())
    {
        MemoryAllocator = MemoryAllocator.Create(new MemoryAllocatorOptions
        {
            AllocationLimitMegabytes = 256,
            MaximumPoolSizeMegabytes = 64
        }),
        MaxDegreeOfParallelism = 1
    };

    private static readonly DecoderOptions DecodeOptions = new()
    {
        Configuration = DecodeConfiguration,
        MaxFrames = 1,
        SkipMetadata = true
    };

    internal static bool IsWithinDecodeLimits(int width, int height) =>
        width is > 0 and <= MaxInputDimension &&
        height is > 0 and <= MaxInputDimension &&
        (long)width * height <= MaxInputPixels;

    /// <summary>
    /// Decodes at most one frame with the restricted configuration, after checking the
    /// header-declared dimensions. Returns null when the image exceeds the decode limits.
    /// </summary>
    internal static async Task<Image?> DecodeBoundedAsync(Stream inputStream, CancellationToken ct)
    {
        if (!inputStream.CanSeek)
        {
            var buffered = new MemoryStream();
            await inputStream.CopyToAsync(buffered, ct);
            buffered.Position = 0;
            inputStream = buffered;
        }

        var start = inputStream.Position;
        var info = await Image.IdentifyAsync(DecodeOptions, inputStream, ct);
        if (!IsWithinDecodeLimits(info.Width, info.Height))
        {
            return null;
        }

        inputStream.Position = start;
        return await Image.LoadAsync(DecodeOptions, inputStream, ct);
    }

    /// <summary>
    /// Processes an image: strips metadata (EXIF), resizes if too large, and converts to optimized JPEG.
    /// </summary>
    public async Task<MemoryStream?> ProcessAsync(Stream inputStream, CancellationToken ct = default)
    {
        var gateEntered = false;
        try
        {
            gateEntered = await DecodeGate.WaitAsync(DecodeQueueTimeout, ct);
            if (!gateEntered)
            {
                logger.LogWarning("Image processing skipped: decode queue is saturated");
                return null;
            }

            using var decodeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            decodeCts.CancelAfter(DecodeTimeout);

            using var image = await DecodeBoundedAsync(inputStream, decodeCts.Token);
            if (image is null)
            {
                logger.LogWarning("Image rejected: dimensions exceed decode limits");
                return null;
            }

            var outputStream = new MemoryStream();

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image processing failed");
            return null;
        }
        finally
        {
            if (gateEntered)
            {
                DecodeGate.Release();
            }
        }
    }
}
