using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Karar.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace Karar.UnitTests.Security;

/// <summary>
/// POST /api/v1/posts görseli yalnızca sıkıştırılmış bayt boyutu (5MB) ve magic byte ile
/// kontrol ediliyordu; ImageSharp ise tüm pikselleri ve tüm animasyon karelerini çözüyordu.
/// Birkaç MB'lık bir PNG/WebP "decompression bomb" API örneğini OOM ile düşürebiliyordu.
/// Bu testler, çözme işleminden önce boyut/kare/format sınırlarının uygulandığını doğrular.
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
        using var source = new Image<L8>(8100, 4);
        var png = Encode(source, new PngEncoder());

        using var result = await CreateService().ProcessAsync(new MemoryStream(png));

        result.Should().BeNull();
    }

    [Fact]
    public async Task ProcessAsync_RejectsImageWhosePixelCountExceedsDecodeLimit()
    {
        // 7000x7000 = 49M pixels: each side is under 8000 but the total is over 40M.
        // 1-bit grayscale rows of zeros compress to a few KB.
        var bomb = BuildPng(7000, 7000, colorType: 0, bitDepth: 1, rowsToWrite: 7000);
        bomb.Length.Should().BeLessThan(256 * 1024);

        using var result = await CreateService().ProcessAsync(new MemoryStream(bomb));

        result.Should().BeNull();
    }

    // ── Frame bombs (animated WebP / APNG) ─────────────────────────────────

    [Theory]
    [InlineData("webp")]
    [InlineData("png")]
    public async Task DecodeBoundedAsync_DecodesOnlyFirstFrameOfAnimatedImage(string format)
    {
        using var animation = new Image<Rgba32>(64, 64, new Rgba32(10, 10, 10, 255));
        for (var i = 1; i < 6; i++)
        {
            using var frame = new Image<Rgba32>(64, 64, new Rgba32((byte)(i * 40), 0, 0, 255));
            animation.Frames.AddFrame(frame.Frames.RootFrame);
        }

        var bytes = format == "webp"
            ? Encode(animation, new WebpEncoder { FileFormat = WebpFileFormatType.Lossless })
            : Encode(animation, new PngEncoder());
        using (var all = Image.Load(bytes))
        {
            all.Frames.Count.Should().Be(6, "the fixture must really be animated");
        }

        using var decoded = await ImageProcessorService.DecodeBoundedAsync(new MemoryStream(bytes), CancellationToken.None);

        decoded.Should().NotBeNull();
        decoded!.Frames.Count.Should().Be(1, "every extra frame is a full canvas-sized pixel buffer");
    }

    // ── Format allow-list (decoder attack surface) ─────────────────────────

    [Theory]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("tiff")]
    public async Task ProcessAsync_RejectsFormatsOutsideJpegPngWebpAllowList(string format)
    {
        using var source = new Image<Rgba32>(16, 16, new Rgba32(200, 30, 30, 255));
        using var buffer = new MemoryStream();
        switch (format)
        {
            case "bmp": source.SaveAsBmp(buffer); break;
            case "gif": source.SaveAsGif(buffer); break;
            default: source.SaveAsTiff(buffer); break;
        }

        using var result = await CreateService().ProcessAsync(new MemoryStream(buffer.ToArray()));

        result.Should().BeNull($"{format} is not an accepted upload format and its decoder must not run");
    }

    // ── Legitimate uploads keep working ────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_DownscalesLargePhotoToMaxDimension()
    {
        using var source = new Image<Rgb24>(2400, 1600, new Rgb24(10, 120, 200));
        using var buffer = new MemoryStream();
        await source.SaveAsJpegAsync(buffer);

        using var result = await CreateService().ProcessAsync(new MemoryStream(buffer.ToArray()));

        result.Should().NotBeNull();
        var info = Image.Identify(result!);
        info.Width.Should().Be(1200);
        info.Height.Should().Be(800);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task ProcessAsync_KeepsSmallImageSizeAndReencodesAsJpeg(string format)
    {
        using var source = new Image<Rgba32>(800, 600, new Rgba32(10, 120, 200, 255));
        var bytes = format == "png" ? Encode(source, new PngEncoder()) : Encode(source, new WebpEncoder());

        using var result = await CreateService().ProcessAsync(new MemoryStream(bytes));

        result.Should().NotBeNull();
        var info = Image.Identify(result!);
        info.Metadata.DecodedImageFormat!.Name.Should().Be("JPEG");
        info.Width.Should().Be(800);
        info.Height.Should().Be(600);
    }

    [Fact]
    public async Task ProcessAsync_AcceptsImageAtExactDimensionLimit()
    {
        using var source = new Image<L8>(8000, 4);
        var png = Encode(source, new PngEncoder());

        using var result = await CreateService().ProcessAsync(new MemoryStream(png));

        result.Should().NotBeNull();
    }

    // ── Cancellation ───────────────────────────────────────────────────────

    [Fact]
    public async Task ProcessAsync_StopsWhenRequestIsAborted()
    {
        using var source = new Image<Rgb24>(64, 64);
        using var buffer = new MemoryStream();
        await source.SaveAsJpegAsync(buffer);
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();

        using var result = await CreateService().ProcessAsync(new MemoryStream(buffer.ToArray()), aborted.Token);

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
    public void ImageProcessor_SerialisesDecodesAndCapsAllocations()
    {
        var service = TestRepoPaths.ReadText("backend", "Karar.Api", "Services", "ImageProcessorService.cs");

        service.Should().Contain("static readonly SemaphoreSlim");
        service.Should().Contain("AllocationLimitMegabytes");
        service.Should().Contain("MaxFrames = 1");
        service.IndexOf("Image.IdentifyAsync(", StringComparison.Ordinal)
            .Should().BeLessThan(service.IndexOf("Image.LoadAsync(", StringComparison.Ordinal));
        service.Should().NotContain("Image.LoadAsync(inputStream, ct)");
    }

    // ── Fixtures ───────────────────────────────────────────────────────────

    private static byte[] Encode(Image image, SixLabors.ImageSharp.Formats.IImageEncoder encoder)
    {
        using var buffer = new MemoryStream();
        image.Save(buffer, encoder);
        return buffer.ToArray();
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
