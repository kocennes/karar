using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Karar.Api.Services;
using Karar.UnitTests.Images;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Karar.UnitTests.Security;

/// <summary>
/// POST /api/v1/posts görseli yalnızca sıkıştırılmış bayt boyutu (5MB) ve magic byte ile
/// kontrol ediliyordu; çözücü ise tüm pikselleri ve tüm animasyon karelerini çözüyordu.
/// Birkaç MB'lık bir PNG/WebP "decompression bomb" API örneğini OOM ile düşürebiliyordu.
/// Bu testler, çözme işleminden önce boyut/kare/format sınırlarının uygulandığını doğrular.
///
/// Entegrasyon notu: #23 bu testleri ImageSharp üzerine yazmıştı; taban dal (861a3c4) ImageSharp'ı
/// SkiaSharp ile değiştirdi. Fikstürler SkiaSharp'a taşındı. ImageSharp'a özgü testlerin
/// (DecodeBoundedAsync tek kare, AllocationLimitMegabytes/MaxFrames kaynak sözleşmesi) karşılığı yok:
/// SKCodec.GetPixels yalnızca varsayılan kareyi (0) çözer. BMP/GIF/TIFF allow-list testi
/// Images/ImageProcessorServiceTests.UnsupportedFormats_AreRejected ile zaten kapsanıyor.
/// </summary>
public sealed class ImageDecompressionBombTests
{
    private static ImageProcessorService CreateService() =>
        new(NullLogger<ImageProcessorService>.Instance);

    // ── Dimension / pixel-count bombs ──────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_RejectsPngDeclaring30000x30000Canvas_WithoutDecodingPixels()
    {
        // 30000x30000 RGBA = ~3.4 GB decoded; the file itself is a few hundred bytes.
        var bomb = BuildPng(30000, 30000, colorType: 6, bitDepth: 8, rowsToWrite: 1);
        bomb.Length.Should().BeLessThan(5 * 1024 * 1024);

        var stopwatch = Stopwatch.StartNew();
        using var result = await CreateService().ProcessAsync(new MemoryStream(bomb));
        stopwatch.Stop();

        result.Should().BeNull("dimensions far beyond the decode limit must be rejected from the header");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_RejectsImageWiderThanDecodeLimit()
    {
        var png = EncodeGray(ImageProcessorService.MaxSourceDimension + 100, 4);

        using var result = await CreateService().ProcessAsync(new MemoryStream(png));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_RejectsImageWhosePixelCountExceedsDecodeLimit()
    {
        // 7000x7000 = 49M pixels: each side is under the per-side limit but the total is over 40M.
        // 1-bit grayscale rows of zeros compress to a few KB.
        var bomb = BuildPng(7000, 7000, colorType: 0, bitDepth: 1, rowsToWrite: 7000);
        bomb.Length.Should().BeLessThan(256 * 1024);

        using var result = await CreateService().ProcessAsync(new MemoryStream(bomb));

        result.Should().BeNull();
    }

    [Fact]
    public void DecodeLimits_AreEnforcedOnEachSideAndOnTotalPixels()
    {
        var max = ImageProcessorService.MaxSourceDimension;
        ImageProcessorService.IsWithinDecodeLimits(max, 4).Should().BeTrue();
        ImageProcessorService.IsWithinDecodeLimits(max + 1, 4).Should().BeFalse();
        ImageProcessorService.IsWithinDecodeLimits(4, max + 1).Should().BeFalse();
        ImageProcessorService.IsWithinDecodeLimits(7000, 7000).Should().BeFalse("49M pixels exceed the 40M budget");
        ImageProcessorService.IsWithinDecodeLimits(0, 10).Should().BeFalse();
    }

    // ── Legitimate uploads keep working ────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_DownscalesLargePhotoToMaxDimension()
    {
        var jpeg = ImageTestData.Encode(2400, 1600, SKEncodedImageFormat.Jpeg, c => c.Clear(new SKColor(10, 120, 200)));

        using var result = await CreateService().ProcessAsync(new MemoryStream(jpeg));

        result.Should().NotBeNull();
        using var codec = SKCodec.Create(new MemoryStream(result!.ToArray()));
        codec.Info.Width.Should().Be(1200);
        codec.Info.Height.Should().Be(800);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task ProcessAsync_KeepsSmallImageSizeAndReencodesAsJpeg(string format)
    {
        var bytes = ImageTestData.Encode(
            800, 600,
            format == "png" ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Webp,
            c => c.Clear(new SKColor(10, 120, 200)));

        using var result = await CreateService().ProcessAsync(new MemoryStream(bytes));

        result.Should().NotBeNull();
        using var codec = SKCodec.Create(new MemoryStream(result!.ToArray()));
        codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Jpeg);
        codec.Info.Width.Should().Be(800);
        codec.Info.Height.Should().Be(600);
    }

    [Fact]
    public async Task ProcessAsync_AcceptsImageAtExactDimensionLimit()
    {
        var png = EncodeGray(ImageProcessorService.MaxSourceDimension, 4);

        using var result = await CreateService().ProcessAsync(new MemoryStream(png));

        result.Should().NotBeNull();
    }

    // ── Cancellation ───────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_StopsWhenRequestIsAborted()
    {
        var jpeg = ImageTestData.Encode(64, 64, SKEncodedImageFormat.Jpeg, c => c.Clear(SKColors.Black));
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        using var result = await CreateService().ProcessAsync(new MemoryStream(jpeg), aborted.Token);

        result.Should().BeNull();
    }

    [Fact]
    public void CreatePostEndpoint_PassesRequestAbortedIntoImageProcessing()
    {
        var program = TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

        program.Contains("imageProcessorService.ProcessAsync(stream, httpRequest.HttpContext.RequestAborted)")
            .Should().BeTrue("decoding must stop when the uploader disconnects");
        program.Contains("imageProcessorService.ProcessAsync(stream);")
            .Should().BeFalse("ProcessAsync must not be called without the request's cancellation token");
    }

    [Fact]
    public void ImageProcessor_SerialisesDecodesAndChecksHeaderBeforeDecoding()
    {
        var service = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "ImageProcessorService.cs");

        service.Should().Contain("static readonly SemaphoreSlim");
        service.Should().Contain("decodeCts.CancelAfter(DecodeTimeout)");
        var limitCheck = service.IndexOf("IsWithinDecodeLimits(sourceWidth, sourceHeight)", StringComparison.Ordinal);
        limitCheck.Should().BePositive();
        limitCheck.Should().BeLessThan(
            service.IndexOf("Decode(codec, scale)", StringComparison.Ordinal),
            "header dimensions must be checked before any pixel buffer is allocated");
        service.Should().NotContain("SKBitmap.Decode(", "a full, unchecked decode bypasses the limits");
    }

    // ── Fixtures ───────────────────────────────────────────────────────────

    private static byte[] EncodeGray(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Gray8, SKAlphaType.Opaque));
        bitmap.Erase(SKColors.Black);
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }

    /// <summary>
    /// Builds a PNG whose IHDR declares <paramref name="width"/>x<paramref name="height"/>
    /// while the IDAT carries only zero-filled rows (which deflate to almost nothing).
    /// </summary>
    private static byte[] BuildPng(int width, int height, byte colorType, byte bitDepth, int rowsToWrite)
    {
        var channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 1 };
        var rowBytes = (int)(((long)width * channels * bitDepth + 7) / 8) + 1; // + filter byte

        using var output = new MemoryStream();
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(0), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        WriteChunk(output, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = new byte[rowBytes];
            for (var y = 0; y < rowsToWrite; y++)
            {
                zlib.Write(row);
            }
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);

        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc32(typeBytes.Concat(data)));
        stream.Write(crcBytes);
    }

    private static uint Crc32(IEnumerable<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
