using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Karar.Api.Services;

public sealed class ShareImageService(ILogger<ShareImageService> logger)
{
    // High-impact colors
    private static readonly SKColor BackgroundColor = SKColor.Parse("#1A1A2E");
    private static readonly SKColor PrimaryColor = SKColor.Parse("#6366F1");
    private static readonly SKColor HakliColor = SKColor.Parse("#22C55E");
    private static readonly SKColor HaksizColor = SKColor.Parse("#EF4444");
    private static readonly SKColor TextColor = SKColors.White;
    private static readonly SKColor SecondaryTextColor = SKColor.Parse("#9CA3AF");
    private static readonly SKColor BarTrackColor = SKColor.Parse("#374151");

    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    private const string FooterText = "Senin kararın ne? Oy vermek için uygulamayı indir.";
    private const string Ellipsis = "…";
    private const float TitleLineSpacing = 1.2f;
    private const int MinVotesForSplit = 40;

    public Task<byte[]?> GeneratePostCardAsync(
        string title,
        string category,
        int hakli,
        int haksiz)
    {
        try
        {
            // 1200x630 is the standard OG image size
            return Task.FromResult<byte[]?>(Render(1200, 630, canvas =>
            {
                canvas.Clear(BackgroundColor);

                using var primaryPaint = CreatePaint(PrimaryColor);
                using var textPaint = CreatePaint(TextColor);
                using var secondaryPaint = CreatePaint(SecondaryTextColor);

                // Draw Top Header (Karar App)
                using (var headerFont = CreateFont(ShareCardFonts.ExtraBold, 32))
                {
                    DrawText(canvas, "karar", 60, 50, headerFont, primaryPaint);
                    DrawText(canvas, ".app", 60 + headerFont.MeasureText("karar"), 50, headerFont, secondaryPaint);
                }

                // Draw Category
                using (var categoryFont = CreateFont(ShareCardFonts.Regular, 24))
                {
                    var categoryText = FitSingleLine(ToDisplayUpper(category, categoryFont), categoryFont, 1080);
                    DrawText(canvas, categoryText, 60, 110, categoryFont, secondaryPaint);
                }

                // Draw Title (Wrapped): 1080px wide box from (60,170) down to the vote labels.
                var (titleFont, titleLines) = FitTitle(title, maxWidth: 1080, maxHeight: 250, sizes: [56f, 48f]);
                using (titleFont)
                {
                    DrawLines(canvas, titleLines, 60, 170, titleFont, textPaint, SKTextAlign.Left);
                }

                // Draw Vote Section
                int total = hakli + haksiz;
                if (total >= MinVotesForSplit)
                {
                    int hakliPct = (int)Math.Round(hakli * 100.0 / total);
                    int haksizPct = 100 - hakliPct;

                    DrawVoteBar(canvas, 60, 480, 1080, 60, hakliPct, haksizPct);

                    // Percent Labels
                    using var pctFont = CreateFont(ShareCardFonts.Bold, 28);
                    DrawText(canvas, $"HAKLI %{hakliPct}", 60, 435, pctFont, textPaint);
                    DrawText(canvas, $"HAKSIZ %{haksizPct}", 1140, 435, pctFont, textPaint, SKTextAlign.Right);
                }
                else
                {
                    using var countFont = CreateFont(ShareCardFonts.Regular, 36);
                    DrawText(canvas, $"{total} OY - Karar veriliyor...", 60, 480, countFont, secondaryPaint);
                }

                // Footer
                using var footerFont = CreateFont(ShareCardFonts.Regular, 20);
                DrawText(canvas, FooterText, 60, 570, footerFont, secondaryPaint);
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Share image generation failed");
            return Task.FromResult<byte[]?>(null);
        }
    }

    public Task<byte[]?> GenerateStoryCardAsync(
        string title,
        string category,
        int hakli,
        int haksiz)
    {
        try
        {
            // 1080x1920 is the standard Instagram Story size
            return Task.FromResult<byte[]?>(Render(1080, 1920, canvas =>
            {
                canvas.Clear(BackgroundColor);

                using var primaryPaint = CreatePaint(PrimaryColor);
                using var textPaint = CreatePaint(TextColor);
                using var secondaryPaint = CreatePaint(SecondaryTextColor);

                // Draw Top Header (Karar App) centered
                using (var headerFont = CreateFont(ShareCardFonts.ExtraBold, 48))
                {
                    var brandWidth = headerFont.MeasureText("karar");
                    var startX = 540 - (brandWidth + headerFont.MeasureText(".app")) / 2;
                    DrawText(canvas, "karar", startX, 150, headerFont, primaryPaint);
                    DrawText(canvas, ".app", startX + brandWidth, 150, headerFont, secondaryPaint);
                }

                // Draw Category (Centered)
                using (var categoryFont = CreateFont(ShareCardFonts.Regular, 36))
                {
                    var categoryText = FitSingleLine(ToDisplayUpper(category, categoryFont), categoryFont, 900);
                    DrawText(canvas, categoryText, 540, 250, categoryFont, secondaryPaint, SKTextAlign.Center);
                }

                // Draw Title (Wrapped, centered horizontally in the 900px box; block centered on y=450
                // but never pushed above the category line).
                var (titleFont, titleLines) = FitTitle(title, maxWidth: 900, maxHeight: 960, sizes: [72f, 60f], maxLines: 8);
                using (titleFont)
                {
                    var blockHeight = titleLines.Count * titleFont.Size * TitleLineSpacing;
                    var top = Math.Max(330f, 450f - blockHeight / 2);
                    DrawLines(canvas, titleLines, 540, top, titleFont, textPaint, SKTextAlign.Center);
                }

                // Draw Vote Section (Bottom part)
                int total = hakli + haksiz;
                if (total >= MinVotesForSplit)
                {
                    int hakliPct = (int)Math.Round(hakli * 100.0 / total);
                    int haksizPct = 100 - hakliPct;

                    DrawVoteBar(canvas, 90, 1400, 900, 100, hakliPct, haksizPct);

                    // Percent Labels
                    using var pctFont = CreateFont(ShareCardFonts.Bold, 40);
                    DrawText(canvas, $"HAKLI %{hakliPct}", 90, 1340, pctFont, textPaint);
                    DrawText(canvas, $"HAKSIZ %{haksizPct}", 990, 1340, pctFont, textPaint, SKTextAlign.Right);
                }
                else
                {
                    using var countFont = CreateFont(ShareCardFonts.Regular, 48);
                    DrawText(canvas, $"{total} OY - Karar veriliyor...", 90, 1400, countFont, secondaryPaint);
                }

                // Footer
                using var footerFont = CreateFont(ShareCardFonts.Regular, 32);
                DrawText(canvas, FooterText, 540, 1750, footerFont, secondaryPaint, SKTextAlign.Center);
            }));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Story image generation failed");
            return Task.FromResult<byte[]?>(null);
        }
    }

    private static byte[] Render(int width, int height, Action<SKCanvas> draw)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque))
            ?? throw new InvalidOperationException("Could not allocate the share image surface.");

        draw(surface.Canvas);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding returned no data.");
        return png.ToArray();
    }

    private static void DrawVoteBar(SKCanvas canvas, float x, float y, float width, float height, int hakliPct, int haksizPct)
    {
        using var trackPaint = CreatePaint(BarTrackColor);
        canvas.DrawRect(x, y, width, height, trackPaint);

        float hakliWidth = width * hakliPct / 100f;
        if (hakliPct > 0)
        {
            using var hakliPaint = CreatePaint(HakliColor);
            canvas.DrawRect(x, y, hakliWidth, height, hakliPaint);
        }

        if (haksizPct > 0)
        {
            using var haksizPaint = CreatePaint(HaksizColor);
            canvas.DrawRect(x + hakliWidth, y, width - hakliWidth, height, haksizPaint);
        }
    }

    private static SKPaint CreatePaint(SKColor color) => new()
    {
        Color = color,
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private static SKFont CreateFont(SKTypeface typeface, float size) => new(typeface, size)
    {
        Edging = SKFontEdging.Antialias,
        Subpixel = true,
        // Unhinted outlines keep measurement and rendering identical on Windows and Linux.
        Hinting = SKFontHinting.None
    };

    /// <summary>
    /// Draws a single line whose top edge (ascender line) sits at <paramref name="top"/>,
    /// matching the old top-left text origin semantics; <paramref name="x"/> is the anchor for the alignment.
    /// </summary>
    private static void DrawText(SKCanvas canvas, string text, float x, float top, SKFont font, SKPaint paint, SKTextAlign align = SKTextAlign.Left)
    {
        if (text.Length == 0)
        {
            return;
        }

        canvas.DrawText(text, x, top - font.Metrics.Ascent, align, font, paint);
    }

    private static void DrawLines(SKCanvas canvas, IReadOnlyList<string> lines, float x, float top, SKFont font, SKPaint paint, SKTextAlign align)
    {
        var lineHeight = font.Size * TitleLineSpacing;
        for (var i = 0; i < lines.Count; i++)
        {
            DrawText(canvas, lines[i], x, top + i * lineHeight, font, paint, align);
        }
    }

    /// <summary>
    /// Picks the largest candidate size whose wrapped title fits the box; the last size falls back to an ellipsis.
    /// The caller owns (disposes) the returned font.
    /// </summary>
    private static (SKFont Font, List<string> Lines) FitTitle(string title, float maxWidth, float maxHeight, float[] sizes, int maxLines = int.MaxValue)
    {
        for (var i = 0; i < sizes.Length; i++)
        {
            var font = CreateFont(ShareCardFonts.ExtraBold, sizes[i]);
            var linesThatFit = Math.Clamp((int)(maxHeight / (sizes[i] * TitleLineSpacing)), 1, maxLines);
            var lines = WrapText(SanitizeText(title, font), font, maxWidth, linesThatFit, out var truncated);
            if (!truncated || i == sizes.Length - 1)
            {
                return (font, lines);
            }

            font.Dispose();
        }

        throw new ArgumentException("At least one font size is required.", nameof(sizes));
    }

    /// <summary>
    /// Turkish-aware uppercase (i → İ, ı → I) after dropping characters the typeface cannot draw.
    /// </summary>
    internal static string ToDisplayUpper(string text, SKFont font) =>
        SanitizeText(text, font).ToUpper(TurkishCulture);

    /// <summary>
    /// Normalizes to NFC (so ğ/ş/İ use their precomposed glyphs), turns control characters into spaces,
    /// drops format characters and characters the typeface has no glyph for (emoji etc. would render
    /// as tofu boxes) and collapses whitespace.
    /// </summary>
    internal static string SanitizeText(string? text, SKFont font)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);
        var builder = new StringBuilder(normalized.Length);
        var pendingSpace = false;
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.IsWhiteSpace(rune) || Rune.IsControl(rune))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            // Invisible format characters (ZWJ, soft hyphen, bidi overrides) have no meaning without shaping.
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format || !font.ContainsGlyph(rune.Value))
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Greedy word wrap measured with the real font. Words wider than the box are broken between
    /// text elements; when more than <paramref name="maxLines"/> lines are needed the last kept line ends with "…".
    /// </summary>
    internal static List<string> WrapText(string text, SKFont font, float maxWidth, int maxLines, out bool truncated)
    {
        truncated = false;
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (font.MeasureText(candidate) <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            current = word;
            while (font.MeasureText(current) > maxWidth)
            {
                var fit = FitPrefixLength(current, font, maxWidth);
                lines.Add(current[..fit]);
                current = current[fit..];
            }

            if (lines.Count > maxLines)
            {
                break;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        if (lines.Count <= maxLines)
        {
            return lines;
        }

        truncated = true;
        var kept = lines.GetRange(0, maxLines);
        kept[^1] = AppendEllipsis(kept[^1], font, maxWidth);
        return kept;
    }

    /// <summary>
    /// Returns the text unchanged when it fits on one line, otherwise the longest prefix that fits followed by "…".
    /// </summary>
    internal static string FitSingleLine(string text, SKFont font, float maxWidth) =>
        font.MeasureText(text) <= maxWidth ? text : AppendEllipsis(text, font, maxWidth);

    private static string AppendEllipsis(string text, SKFont font, float maxWidth)
    {
        var ellipsis = font.ContainsGlyph(Ellipsis[0]) ? Ellipsis : "...";
        var elementStarts = StringInfo.ParseCombiningCharacters(text);
        for (var count = elementStarts.Length; count > 0; count--)
        {
            var end = count == elementStarts.Length ? text.Length : elementStarts[count];
            var candidate = text[..end].TrimEnd(' ', ',', '.', ';', ':', '-') + ellipsis;
            if (font.MeasureText(candidate) <= maxWidth)
            {
                return candidate;
            }
        }

        return ellipsis;
    }

    private static int FitPrefixLength(string text, SKFont font, float maxWidth)
    {
        var elementStarts = StringInfo.ParseCombiningCharacters(text);
        var fit = elementStarts.Length > 1 ? elementStarts[1] : text.Length;
        for (var i = 2; i <= elementStarts.Length; i++)
        {
            var end = i == elementStarts.Length ? text.Length : elementStarts[i];
            if (font.MeasureText(text[..end]) > maxWidth)
            {
                break;
            }

            fit = end;
        }

        return fit;
    }
}
