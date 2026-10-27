using System.Buffers.Binary;
using System.Text;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace Karar.UnitTests.Images;

/// <summary>
/// Builds real and deliberately hostile image payloads for the image pipeline tests.
/// </summary>
internal static class ImageTestData
{
    public const string ExifCameraMake = "KararCam";

    public static byte[] Encode(int width, int height, SKEncodedImageFormat format, Action<SKCanvas> draw, bool opaque = true, int quality = 90)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, opaque ? SKAlphaType.Opaque : SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var data = image.Encode(format, quality);
        return data.ToArray();
    }

    /// <summary>
    /// Inserts an EXIF APP1 segment (camera make, orientation, GPS IFD) right after the JFIF APP0 segment.
    /// </summary>
    public static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        var tiff = new List<byte>();
        void U16(ushort v) { tiff.Add((byte)(v >> 8)); tiff.Add((byte)v); }
        void U32(uint v) { U16((ushort)(v >> 16)); U16((ushort)v); }

        // TIFF header (big endian), IFD0 at offset 8.
        tiff.AddRange("MM"u8.ToArray());
        U16(42);
        U32(8);

        // IFD0: Make, Orientation, GPS IFD pointer (sorted by tag).
        U16(3);
        U16(0x010F); U16(2); U32((uint)ExifCameraMake.Length + 1); U32(80);
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        U16(0x8825); U16(4); U32(1); U32(50);
        U32(0);

        // GPS IFD at offset 50: GPSVersionID + GPSLatitudeRef.
        U16(2);
        U16(0x0000); U16(1); U32(4); tiff.AddRange(new byte[] { 2, 3, 0, 0 });
        U16(0x0001); U16(2); U32(2); tiff.AddRange(new byte[] { (byte)'N', 0, 0, 0 });
        U32(0);

        // Make string at offset 80.
        tiff.AddRange(Encoding.ASCII.GetBytes(ExifCameraMake + "\0"));

        var payload = "Exif\0\0"u8.ToArray().Concat(tiff).ToArray();
        var segment = new List<byte> { 0xFF, 0xE1, (byte)((payload.Length + 2) >> 8), (byte)(payload.Length + 2) };
        segment.AddRange(payload);

        Assert.True(jpeg[0] == 0xFF && jpeg[1] == 0xD8, "Source must be a JPEG.");
        var insertAt = 2;
        if (jpeg[2] == 0xFF && jpeg[3] == 0xE0)
        {
            insertAt = 4 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(4, 2));
        }

        return jpeg[..insertAt].Concat(segment).Concat(jpeg[insertAt..]).ToArray();
    }

    /// <summary>
    /// Marker bytes of every JPEG segment before the first scan (SOS).
    /// </summary>
    public static List<byte> JpegHeaderMarkers(byte[] jpeg)
    {
        var markers = new List<byte>();
        var offset = 2;
        while (offset + 4 <= jpeg.Length && jpeg[offset] == 0xFF)
        {
            var marker = jpeg[offset + 1];
            markers.Add(marker);
            if (marker == 0xDA)
            {
                break;
            }

            offset += 2 + BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(offset + 2, 2));
        }

        return markers;
    }

    public static bool Contains(byte[] haystack, string ascii) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(ascii)) >= 0;

    /// <summary>
    /// A PNG whose IHDR claims the given size but carries (almost) no pixel data — a decompression-bomb probe.
    /// </summary>
    public static byte[] PngHeaderOnly(int width, int height)
    {
        var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 6;  // RGBA
        AddChunk(bytes, "IHDR", ihdr);
        AddChunk(bytes, "IDAT", [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01]);
        AddChunk(bytes, "IEND", []);
        return bytes.ToArray();
    }

    public static byte[] Gif1x1() =>
    [
        .. "GIF89a"u8, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
        0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF,
        0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
        0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
    ];

    public static byte[] Bmp1x1()
    {
        var bmp = new byte[58];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(2), bmp.Length);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(10), 54);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(18), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(22), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bmp.AsSpan(28), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(34), 4);
        bmp[54] = 0xFF;
        return bmp;
    }

    public static byte[] TiffHeader() =>
    [
        (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x10, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00
    ];

    private static void AddChunk(List<byte> png, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.AddRange(length);

        var typeAndData = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        png.AddRange(typeAndData);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData));
        png.AddRange(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (Entries)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
