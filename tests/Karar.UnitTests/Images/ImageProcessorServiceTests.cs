using System.Diagnostics;
using Karar.Api.Services;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Karar.UnitTests.Images;

public sealed class ImageProcessorServiceTests
{
    private readonly CapturingLogger<ImageProcessorService> _logger = new();

    private ImageProcessorService CreateService() => new(_logger);

    private async Task<byte[]?> ProcessAsync(byte[] input)
    {
        using var stream = new MemoryStream(input);
        using var output = await CreateService().ProcessAsync(stream);
        return output?.ToArray();
    }

    private static SKBitmap DecodeJpeg(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        return SKBitmap.Decode(bytes);
    }

    [Fact]
    public async Task Jpeg_WithExifAndGps_IsDownscaledAndStrippedOfAllMetadata()
    {
        var source = ImageTestData.WithExif(
            ImageTestData.Encode(4000, 3000, SKEncodedImageFormat.Jpeg, c => c.Clear(new SKColor(30, 120, 200))),
            orientation: 1);
        Assert.True(ImageTestData.Contains(source, "Exif"));
        Assert.True(ImageTestData.Contains(source, ImageTestData.ExifCameraMake));

        var output = await ProcessAsync(source);

        Assert.NotNull(output);
        Assert.Equal(new byte[] { 0xFF, 0xD8, 0xFF }, output[..3]);
        Assert.False(ImageTestData.Contains(output, "Exif"));
        Assert.False(ImageTestData.Contains(output, ImageTestData.ExifCameraMake));
        Assert.False(ImageTestData.Contains(output, "ICC_PROFILE"));
        Assert.False(ImageTestData.Contains(output, "http://ns.adobe.com/xap"));

        // Only JFIF (APP0) is allowed among application segments: no APP1 (EXIF/XMP), APP2 (ICC), APP13 (IPTC), COM.
        var markers = ImageTestData.JpegHeaderMarkers(output);
        Assert.Contains((byte)0xDA, markers);
        Assert.DoesNotContain(markers, m => m is >= 0xE1 and <= 0xEF || m == 0xFE);

        using var decoded = DecodeJpeg(output);
        Assert.Equal(1200, decoded.Width);
        Assert.Equal(900, decoded.Height);
    }

    [Fact]
    public async Task Jpeg_WithExifOrientation6_IsRotatedUpright()
    {
        // Stored 300x100: left half red, right half blue. Orientation 6 = rotate 90° clockwise for display.
        var source = ImageTestData.WithExif(
            ImageTestData.Encode(300, 100, SKEncodedImageFormat.Jpeg, c =>
            {
                using var red = new SKPaint { Color = SKColors.Red };
                using var blue = new SKPaint { Color = SKColors.Blue };
                c.DrawRect(0, 0, 150, 100, red);
                c.DrawRect(150, 0, 150, 100, blue);
            }),
            orientation: 6);

        var output = await ProcessAsync(source);

        Assert.NotNull(output);
        Assert.False(ImageTestData.Contains(output, "Exif"));
        using var decoded = DecodeJpeg(output);
        Assert.Equal(100, decoded.Width);
        Assert.Equal(300, decoded.Height);

        var top = decoded.GetPixel(50, 40);
        var bottom = decoded.GetPixel(50, 260);
        Assert.True(top.Red > 200 && top.Blue < 60, $"Top should be red but was {top}");
        Assert.True(bottom.Blue > 200 && bottom.Red < 60, $"Bottom should be blue but was {bottom}");
    }

    [Fact]
    public async Task LargePngWithAlpha_IsFlattenedOnWhiteAndDownscaled()
    {
        var source = ImageTestData.Encode(3000, 2000, SKEncodedImageFormat.Png, c =>
        {
            using var red = new SKPaint { Color = SKColors.Red };
            c.DrawRect(1000, 500, 1000, 1000, red);
        }, opaque: false);

        var output = await ProcessAsync(source);

        Assert.NotNull(output);
        using var decoded = DecodeJpeg(output);
        Assert.Equal(1200, decoded.Width);
        Assert.Equal(800, decoded.Height);

        var corner = decoded.GetPixel(20, 20);
        Assert.True(corner.Red > 245 && corner.Green > 245 && corner.Blue > 245, $"Transparent area should be white but was {corner}");
        var center = decoded.GetPixel(600, 400);
        Assert.True(center.Red > 200 && center.Green < 60 && center.Blue < 60, $"Opaque area should stay red but was {center}");
    }

    [Fact]
    public async Task SmallWebp_IsConvertedToJpegWithoutUpscaling()
    {
        var source = ImageTestData.Encode(800, 600, SKEncodedImageFormat.Webp, c => c.Clear(SKColors.Orange));

        var output = await ProcessAsync(source);

        Assert.NotNull(output);
        using var decoded = DecodeJpeg(output);
        Assert.Equal(800, decoded.Width);
        Assert.Equal(600, decoded.Height);
    }

    public static TheoryData<string, byte[]> UnsupportedFormats() => new()
    {
        { "gif", ImageTestData.Gif1x1() },
        { "bmp", ImageTestData.Bmp1x1() },
        { "tiff", ImageTestData.TiffHeader() },
        { "text", "<svg xmlns='http://www.w3.org/2000/svg'/>"u8.ToArray() },
        { "empty", [] }
    };

    [Theory]
    [MemberData(nameof(UnsupportedFormats))]
    public async Task UnsupportedFormats_AreRejected(string format, byte[] input)
    {
        var output = await ProcessAsync(input);

        Assert.True(output is null, $"{format} must be rejected");
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData(50_000, 50_000)]
    [InlineData(12_000, 100)]
    [InlineData(7_000, 7_000)]
    public async Task OversizedPngHeader_IsRejectedBeforeDecoding(int width, int height)
    {
        var stopwatch = Stopwatch.StartNew();

        var output = await ProcessAsync(ImageTestData.PngHeaderOnly(width, height));

        stopwatch.Stop();
        Assert.Null(output);
        Assert.Contains(_logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains($"{width}x{height}") && e.Message.Contains("exceed decode limits"));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Rejection took {stopwatch.Elapsed}");
    }

    [Fact]
    public async Task TruncatedJpeg_IsRejected()
    {
        var jpeg = ImageTestData.Encode(1000, 800, SKEncodedImageFormat.Jpeg, c =>
        {
            using var paint = new SKPaint { Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1000, 800), [SKColors.Red, SKColors.Blue], SKShaderTileMode.Clamp) };
            c.DrawRect(0, 0, 1000, 800, paint);
        });

        var output = await ProcessAsync(jpeg[..(jpeg.Length / 3)]);

        Assert.Null(output);
    }

    [Fact]
    public async Task InputLargerThanLimit_IsRejectedWithoutDecoding()
    {
        var input = new byte[ImageProcessorService.MaxInputBytes + 1];
        input[0] = 0xFF;
        input[1] = 0xD8;
        input[2] = 0xFF;

        var output = await ProcessAsync(input);

        Assert.Null(output);
        Assert.Contains(_logger.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("exceeds"));
    }
}
