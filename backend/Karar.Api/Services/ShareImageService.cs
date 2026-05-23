using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Karar.Api.Services;

public sealed class ShareImageService(ILogger<ShareImageService> logger)
{
    // High-impact colors
    private static readonly Color BackgroundColor = Color.ParseHex("#1A1A2E");
    private static readonly Color PrimaryColor = Color.ParseHex("#6366F1");
    private static readonly Color HakliColor = Color.ParseHex("#22C55E");
    private static readonly Color HaksizColor = Color.ParseHex("#EF4444");
    private static readonly Color TextColor = Color.White;
    private static readonly Color SecondaryTextColor = Color.ParseHex("#9CA3AF");

    public async Task<byte[]?> GeneratePostCardAsync(
        string title,
        string category,
        int hakli,
        int haksiz)
    {
        try
        {
            // 1200x630 is the standard OG image size
            using var image = new Image<Rgba32>(1200, 630);

            image.Mutate(ctx =>
            {
                ctx.Fill(BackgroundColor);

                // Draw Top Header (Karar App)
                var headerFont = SystemFonts.CreateFont("Arial", 32, FontStyle.Bold);
                ctx.DrawText("karar", headerFont, PrimaryColor, new PointF(60, 50));
                ctx.DrawText(".app", headerFont, SecondaryTextColor, new PointF(145, 50));

                // Draw Category
                var categoryFont = SystemFonts.CreateFont("Arial", 24, FontStyle.Regular);
                ctx.DrawText(category.ToUpper(), categoryFont, SecondaryTextColor, new PointF(60, 110));

                // Draw Title (Wrapped)
                var titleFont = SystemFonts.CreateFont("Arial", 56, FontStyle.Bold);
                var richTextOptions = new RichTextOptions(titleFont)
                {
                    WrappingLength = 1080,
                    Origin = new PointF(60, 170),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                ctx.DrawText(richTextOptions, title, TextColor);

                // Draw Vote Section
                int total = hakli + haksiz;
                if (total >= 40)
                {
                    int hakliPct = (int)Math.Round(hakli * 100.0 / total);
                    int haksizPct = 100 - hakliPct;

                    // Progress Bar Background
                    ctx.Fill(Color.ParseHex("#374151"), new RectangularPolygon(60, 480, 1080, 60));

                    // Hakli Bar
                    if (hakliPct > 0)
                    {
                        float hakliWidth = (1080f * hakliPct) / 100f;
                        ctx.Fill(HakliColor, new RectangularPolygon(60, 480, hakliWidth, 60));
                    }

                    // Haksiz Bar
                    if (haksizPct > 0)
                    {
                        float hakliWidth = (1080f * hakliPct) / 100f;
                        float haksizWidth = 1080f - hakliWidth;
                        ctx.Fill(HaksizColor, new RectangularPolygon(60 + hakliWidth, 480, haksizWidth, 60));
                    }

                    // Percent Labels
                    var pctFont = SystemFonts.CreateFont("Arial", 28, FontStyle.Bold);
                    ctx.DrawText($"HAKLI %{hakliPct}", pctFont, TextColor, new PointF(60, 435));

                    var haksizText = $"HAKSIZ %{haksizPct}";
                    var haksizMeasureOptions = new TextOptions(pctFont);
                    var haksizSize = TextMeasurer.MeasureSize(haksizText, haksizMeasureOptions);
                    ctx.DrawText(haksizText, pctFont, TextColor, new PointF(1140 - haksizSize.Width, 435));
                }
                else
                {
                    var countFont = SystemFonts.CreateFont("Arial", 36, FontStyle.Italic);
                    ctx.DrawText($"{total} OY - Karar veriliyor...", countFont, SecondaryTextColor, new PointF(60, 480));
                }

                // Footer
                var footerFont = SystemFonts.CreateFont("Arial", 20, FontStyle.Regular);
                ctx.DrawText("Senin kararın ne? Oy vermek için uygulamayı indir.", footerFont, SecondaryTextColor, new PointF(60, 570));
            });

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Share image generation failed");
            return null;
        }
    }

    public async Task<byte[]?> GenerateStoryCardAsync(
        string title,
        string category,
        int hakli,
        int haksiz)
    {
        try
        {
            // 1080x1920 is the standard Instagram Story size
            using var image = new Image<Rgba32>(1080, 1920);

            image.Mutate(ctx =>
            {
                ctx.Fill(BackgroundColor);

                // Draw Top Header (Karar App) centered
                var headerFont = SystemFonts.CreateFont("Arial", 48, FontStyle.Bold);
                ctx.DrawText("karar", headerFont, PrimaryColor, new PointF(380, 150));
                ctx.DrawText(".app", headerFont, SecondaryTextColor, new PointF(510, 150));

                // Draw Category (Centered)
                var categoryFont = SystemFonts.CreateFont("Arial", 36, FontStyle.Regular);
                var categoryText = category.ToUpper();
                var catMeasureOptions = new TextOptions(categoryFont);
                var catSize = TextMeasurer.MeasureSize(categoryText, catMeasureOptions);
                ctx.DrawText(categoryText, categoryFont, SecondaryTextColor, new PointF(540 - catSize.Width / 2, 250));

                // Draw Title (Wrapped, Centered vertically in middle part)
                var titleFont = SystemFonts.CreateFont("Arial", 72, FontStyle.Bold);
                var richTextOptions = new RichTextOptions(titleFont)
                {
                    WrappingLength = 900,
                    Origin = new PointF(90, 450),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ctx.DrawText(richTextOptions, title, TextColor);

                // Draw Vote Section (Bottom part)
                int total = hakli + haksiz;
                if (total >= 40)
                {
                    int hakliPct = (int)Math.Round(hakli * 100.0 / total);
                    int haksizPct = 100 - hakliPct;

                    // Progress Bar Background
                    ctx.Fill(Color.ParseHex("#374151"), new RectangularPolygon(90, 1400, 900, 100));

                    // Hakli Bar
                    if (hakliPct > 0)
                    {
                        float hakliWidth = (900f * hakliPct) / 100f;
                        ctx.Fill(HakliColor, new RectangularPolygon(90, 1400, hakliWidth, 100));
                    }

                    // Haksiz Bar
                    if (haksizPct > 0)
                    {
                        float hakliWidth = (900f * hakliPct) / 100f;
                        float haksizWidth = 900f - hakliWidth;
                        ctx.Fill(HaksizColor, new RectangularPolygon(90 + hakliWidth, 1400, haksizWidth, 100));
                    }

                    // Percent Labels
                    var pctFont = SystemFonts.CreateFont("Arial", 40, FontStyle.Bold);
                    ctx.DrawText($"HAKLI %{hakliPct}", pctFont, TextColor, new PointF(90, 1340));

                    var haksizText = $"HAKSIZ %{haksizPct}";
                    var haksizMeasureOptions = new TextOptions(pctFont);
                    var haksizSize = TextMeasurer.MeasureSize(haksizText, haksizMeasureOptions);
                    ctx.DrawText(haksizText, pctFont, TextColor, new PointF(990 - haksizSize.Width, 1340));
                }
                else
                {
                    var countFont = SystemFonts.CreateFont("Arial", 48, FontStyle.Italic);
                    ctx.DrawText($"{total} OY - Karar veriliyor...", countFont, SecondaryTextColor, new PointF(90, 1400));
                }

                // Footer
                var footerFont = SystemFonts.CreateFont("Arial", 32, FontStyle.Regular);
                var footerText = "Senin kararın ne? Oy vermek için uygulamayı indir.";
                var footerMeasureOptions = new TextOptions(footerFont);
                var footerSize = TextMeasurer.MeasureSize(footerText, footerMeasureOptions);
                ctx.DrawText(footerText, footerFont, SecondaryTextColor, new PointF(540 - footerSize.Width / 2, 1750));
            });

            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Story image generation failed");
            return null;
        }
    }
}
