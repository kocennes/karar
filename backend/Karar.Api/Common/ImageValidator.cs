namespace Karar.Api.Common;

public static class ImageValidator
{
    // Desteklenen formatlar: JPEG, PNG, WebP
    // Dönen değer: "jpeg" | "png" | "webp" | null (tanınamayan/desteklenmeyen format)
    public static string? DetectFormat(Stream stream)
    {
        Span<byte> header = stackalloc byte[12];
        var read = stream.Read(header);
        stream.Position = 0;

        if (read < 4) return null;

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return "jpeg";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (read >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return "png";

        // WebP: RIFF????WEBP
        if (read >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
            return "webp";

        return null;
    }

    // Geriye dönük uyumluluk için extension tabanlı doğrulama da korunuyor.
    public static bool IsValid(Stream stream, string extension)
    {
        var detected = DetectFormat(stream);
        if (detected is null) return false;

        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => detected == "jpeg",
            ".png"            => detected == "png",
            ".webp"           => detected == "webp",
            _                 => false
        };
    }
}
