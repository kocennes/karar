using SkiaSharp;

namespace Karar.Api.Services;

/// <summary>
/// Plus Jakarta Sans typefaces embedded in the assembly (SIL OFL 1.1, see Assets/Fonts/OFL.txt).
/// The runtime container ships no system fonts, so share cards must never depend on them.
/// Each typeface is loaded once and shared; SKTypeface is immutable and thread-safe.
/// </summary>
internal static class ShareCardFonts
{
    private const string ResourcePrefix = "Karar.Api.Fonts.";

    private static readonly Lazy<SKTypeface> RegularTypeface = new(() => Load("PlusJakartaSans-Regular.ttf"));
    private static readonly Lazy<SKTypeface> BoldTypeface = new(() => Load("PlusJakartaSans-Bold.ttf"));
    private static readonly Lazy<SKTypeface> ExtraBoldTypeface = new(() => Load("PlusJakartaSans-ExtraBold.ttf"));

    public static SKTypeface Regular => RegularTypeface.Value;
    public static SKTypeface Bold => BoldTypeface.Value;
    public static SKTypeface ExtraBold => ExtraBoldTypeface.Value;

    private static SKTypeface Load(string fileName)
    {
        var resourceName = ResourcePrefix + fileName;
        using var stream = typeof(ShareCardFonts).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' was not found.");
        using var data = SKData.Create(stream)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' could not be read.");

        return SKTypeface.FromData(data)
            ?? throw new InvalidOperationException($"Embedded font resource '{resourceName}' is not a valid font.");
    }
}
