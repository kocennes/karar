using Karar.Api.Common;
using SkiaSharp;

namespace Karar.Api.Services;

public sealed class ImageProcessorService(ILogger<ImageProcessorService> logger)
{
    private const int MaxDimension = 1200;
    private const int JpegQuality = 75;

    // Decompression-bomb guards: checked against the header before any pixel buffer is allocated.
    internal const int MaxInputBytes = 20 * 1024 * 1024;
    internal const int MaxSourceDimension = 10_000;
    internal const long MaxSourcePixels = 40_000_000;

    private static readonly SKColorSpace Srgb = SKColorSpace.CreateSrgb();

    // 2x2 box average when the scale is exactly 1/2; used for the progressive pre-shrink steps.
    private static readonly SKSamplingOptions HalvingSampling = new(SKFilterMode.Linear, SKMipmapMode.None);
    private static readonly SKSamplingOptions FinalSampling = new(SKCubicResampler.Mitchell);

    /// <summary>
    /// Processes an image: accepts only JPEG/PNG/WebP, rejects oversized headers before decoding,
    /// applies the EXIF orientation, resizes to fit 1200px and re-encodes as a metadata-free JPEG
    /// (no EXIF/GPS/XMP/ICC — only pixels are carried over).
    /// </summary>
    public async Task<MemoryStream?> ProcessAsync(Stream inputStream, CancellationToken ct = default)
    {
        try
        {
            var encoded = await ReadBoundedAsync(inputStream, MaxInputBytes, ct);
            if (encoded is null)
            {
                logger.LogWarning("Image rejected: input exceeds {MaxBytes} bytes", MaxInputBytes);
                return null;
            }

            // Magic-byte allowlist before the bytes reach any native codec (GIF/BMP/ICO/HEIF/... never parsed).
            using (var sniffStream = new MemoryStream(encoded, writable: false))
            {
                if (ImageValidator.DetectFormat(sniffStream) is null)
                {
                    logger.LogWarning("Image rejected: unsupported or unknown format signature");
                    return null;
                }
            }

            using var data = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data);
            if (codec is null)
            {
                logger.LogWarning("Image rejected: data could not be decoded");
                return null;
            }

            if (codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp))
            {
                logger.LogWarning("Image rejected: unsupported encoded format {Format}", codec.EncodedFormat);
                return null;
            }

            var sourceWidth = codec.Info.Width;
            var sourceHeight = codec.Info.Height;
            if (sourceWidth <= 0 || sourceHeight <= 0 ||
                sourceWidth > MaxSourceDimension || sourceHeight > MaxSourceDimension ||
                (long)sourceWidth * sourceHeight > MaxSourcePixels)
            {
                logger.LogWarning(
                    "Image rejected: dimensions {Width}x{Height} exceed decode limits",
                    sourceWidth,
                    sourceHeight);
                return null;
            }

            // Target size in stored (pre-rotation) orientation; the longest side is rotation invariant.
            var scale = Math.Min(1.0, (double)MaxDimension / Math.Max(sourceWidth, sourceHeight));
            var targetWidth = Math.Max(1, (int)Math.Round(sourceWidth * scale));
            var targetHeight = Math.Max(1, (int)Math.Round(sourceHeight * scale));

            using var decoded = Decode(codec, scale);
            if (decoded is null)
            {
                logger.LogWarning("Image rejected: pixel data is corrupt or truncated");
                return null;
            }

            // Progressive 2x box pre-shrink (PNG has no decoder-side scaling), final step uses a cubic filter.
            var working = decoded;
            SKBitmap? shrunk = null;
            try
            {
                while (working.Width >= targetWidth * 2 && working.Height >= targetHeight * 2)
                {
                    var next = new SKBitmap(working.Info.WithSize(working.Width / 2, working.Height / 2));
                    if (!working.ScalePixels(next, HalvingSampling))
                    {
                        next.Dispose();
                        break;
                    }

                    shrunk?.Dispose();
                    shrunk = next;
                    working = next;
                }

                var origin = codec.EncodedOrigin;
                var (outputWidth, outputHeight) = SwapsAxes(origin)
                    ? (targetHeight, targetWidth)
                    : (targetWidth, targetHeight);

                // No color space on the output: the encoder then writes no ICC profile at all.
                using var output = new SKBitmap(new SKImageInfo(
                    outputWidth, outputHeight, SKColorType.Rgba8888, SKAlphaType.Opaque));
                working.SetImmutable(); // lets SKImage share the pixels instead of copying them
                using (var canvas = new SKCanvas(output))
                using (var image = SKImage.FromBitmap(working))
                {
                    // Transparent PNG/WebP areas become white instead of JPEG's implicit black.
                    canvas.Clear(SKColors.White);
                    canvas.SetMatrix(OrientationMatrix(origin, targetWidth, targetHeight));
                    canvas.DrawImage(image, new SKRect(0, 0, targetWidth, targetHeight), FinalSampling);
                }

                var outputStream = new MemoryStream();
                using (var pixmap = output.PeekPixels())
                {
                    var options = new SKJpegEncoderOptions(
                        JpegQuality,
                        SKJpegEncoderDownsample.Downsample420,
                        SKJpegEncoderAlphaOption.Ignore);
                    if (pixmap is null || !pixmap.Encode(outputStream, options))
                    {
                        outputStream.Dispose();
                        logger.LogError("Image processing failed: JPEG encoding returned no data");
                        return null;
                    }
                }

                outputStream.Position = 0;
                return outputStream;
            }
            finally
            {
                shrunk?.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Image processing failed");
            return null;
        }
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream input, int maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk.AsMemory(), ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Decodes into sRGB (embedded ICC profiles are converted, not copied). When the codec can scale
    /// natively (JPEG DCT scaling, WebP) it decodes at the smallest supported size not below the target.
    /// </summary>
    private static SKBitmap? Decode(SKCodec codec, double scale)
    {
        var fullSize = codec.Info.Size;
        if (scale < 1.0)
        {
            var scaled = codec.GetScaledDimensions((float)scale);
            if (scaled.Width > 0 && scaled.Height > 0 &&
                scaled.Width <= fullSize.Width && scaled.Height <= fullSize.Height &&
                scaled != fullSize)
            {
                var bitmap = DecodeAt(codec, scaled, out var scaledResult);
                if (bitmap is not null || scaledResult != SKCodecResult.InvalidScale)
                {
                    return bitmap;
                }
            }
        }

        return DecodeAt(codec, fullSize, out _);
    }

    private static SKBitmap? DecodeAt(SKCodec codec, SKSizeI size, out SKCodecResult result)
    {
        var alphaType = codec.Info.AlphaType == SKAlphaType.Opaque ? SKAlphaType.Opaque : SKAlphaType.Premul;
        var info = new SKImageInfo(size.Width, size.Height, SKImageInfo.PlatformColorType, alphaType, Srgb);
        var bitmap = new SKBitmap(info);
        try
        {
            // Truncated/corrupt data (IncompleteInput, InvalidInput, ...) is rejected rather than half-decoded.
            result = codec.GetPixels(info, bitmap.GetPixels());
            if (result != SKCodecResult.Success)
            {
                bitmap.Dispose();
                return null;
            }

            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static bool SwapsAxes(SKEncodedOrigin origin) => origin is
        SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
        SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

    /// <summary>
    /// Maps the stored pixel grid (w x h) onto the upright output for each EXIF orientation value.
    /// </summary>
    private static SKMatrix OrientationMatrix(SKEncodedOrigin origin, float w, float h) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity
    };
}
