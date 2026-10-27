using Karar.Api.Services;
using SkiaSharp;

namespace Karar.UnitTests.Images;

public sealed class ShareImageServiceTests
{
    private const string TurkishTitle = "Şişli'deki ığdırlı komşum çöpü İstanbul'un ortasına bıraktı, haklı mıyım?";
    private const string TurkishGlyphs = "ĞğÜüŞşİıÖöÇç";

    private static readonly string VeryLongTitle = string.Join(' ', Enumerable.Repeat("Çocuğumun öğretmeniyle yaşadığım şu tartışmada", 12));

    private readonly CapturingLogger<ShareImageService> _logger = new();

    private ShareImageService CreateService() => new(_logger);

    public static TheoryData<string, string, int, int> CardCases() => new()
    {
        { TurkishTitle, "ilişkiler", 63, 37 },
        { TurkishTitle, "İş hayatı", 0, 0 },
        { VeryLongTitle, "aile", 10, 5 },
        { VeryLongTitle, "aile", 400, 120 },
        { new string('Ğ', 300), "çok uzun kategori adı ".PadRight(200, 'ş'), 1, 99 },
        { "Emoji 😅🔥👨‍👩‍👧 ve\u0000kontrol\tkarakterleri\r\n", "sosyal", 40, 0 },
        { "", "", 0, 0 }
    };

    [Theory]
    [MemberData(nameof(CardCases))]
    public async Task PostCard_IsPngOfOgSize(string title, string category, int hakli, int haksiz)
    {
        var bytes = await CreateService().GeneratePostCardAsync(title, category, hakli, haksiz);

        AssertPng(bytes, 1200, 630);
        Assert.Empty(_logger.Entries);
    }

    [Theory]
    [MemberData(nameof(CardCases))]
    public async Task StoryCard_IsPngOfStorySize(string title, string category, int hakli, int haksiz)
    {
        var bytes = await CreateService().GenerateStoryCardAsync(title, category, hakli, haksiz);

        AssertPng(bytes, 1080, 1920);
        Assert.Empty(_logger.Entries);
    }

    [Fact]
    public async Task PostCard_RendersTitleTextAndVoteSplit()
    {
        var bytes = await CreateService().GeneratePostCardAsync(TurkishTitle, "ilişkiler", 63, 37);

        using var bitmap = SKBitmap.Decode(bytes);

        // Title glyphs must actually be drawn (the embedded font loaded): white pixels in the title box.
        Assert.True(CountNearWhite(bitmap, new SKRectI(60, 170, 1140, 420)) > 2_000, "Title text was not rendered");

        // 63/37 split: green up to 63% of the 1080px bar, red after it.
        AssertColorNear(bitmap.GetPixel(60 + 600, 510), SKColor.Parse("#22C55E"));
        AssertColorNear(bitmap.GetPixel(60 + 700, 510), SKColor.Parse("#EF4444"));
    }

    [Fact]
    public async Task StoryCard_WithoutEnoughVotes_HasNoVoteBar()
    {
        var bytes = await CreateService().GenerateStoryCardAsync(TurkishTitle, "aile", 3, 2);

        using var bitmap = SKBitmap.Decode(bytes);

        AssertColorNear(bitmap.GetPixel(900, 1480), SKColor.Parse("#1A1A2E"));
    }

    [Fact]
    public void EmbeddedFonts_CoverTurkishAlphabet()
    {
        foreach (var typeface in new[] { ShareCardFonts.Regular, ShareCardFonts.Bold, ShareCardFonts.ExtraBold })
        {
            using var font = new SKFont(typeface, 24);
            Assert.Equal("Plus Jakarta Sans", typeface.FamilyName);
            Assert.True(font.ContainsGlyphs(TurkishGlyphs), $"{typeface.FamilyName} {typeface.FontWeight} lacks Turkish glyphs");
            Assert.True(font.ContainsGlyph('…'));
        }
    }

    [Theory]
    [InlineData("ilişkiler", "İLİŞKİLER")]
    [InlineData("iş hayatı", "İŞ HAYATI")]
    [InlineData("ığdır çiçeği", "IĞDIR ÇİÇEĞİ")]
    public void ToDisplayUpper_UsesTurkishCasing(string input, string expected)
    {
        using var font = new SKFont(ShareCardFonts.Regular, 24);

        Assert.Equal(expected, ShareImageService.ToDisplayUpper(input, font));
    }

    [Fact]
    public void SanitizeText_DropsGlyphlessCharactersAndCollapsesWhitespace()
    {
        using var font = new SKFont(ShareCardFonts.Regular, 24);

        var sanitized = ShareImageService.SanitizeText("  Haklı mıyım? 😅\r\n\tEvet\u0000 ", font);

        Assert.Equal("Haklı mıyım? Evet", sanitized);
        Assert.Equal("abc", ShareImageService.SanitizeText("a‍b‮c­", font));
        // Decomposed input (S + combining cedilla) is normalized to the precomposed glyph.
        Assert.Equal("Ş", ShareImageService.SanitizeText("Ş", font));
    }

    [Fact]
    public void WrapText_KeepsEveryLineInsideTheBoxAndEllipsizesOverflow()
    {
        using var font = new SKFont(ShareCardFonts.ExtraBold, 56);

        var lines = ShareImageService.WrapText(VeryLongTitle, font, 1080, 3, out var truncated);

        Assert.True(truncated);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.True(font.MeasureText(line) <= 1080, $"'{line}' overflows"));
        Assert.EndsWith("…", lines[^1]);
    }

    [Fact]
    public void WrapText_BreaksWordsLongerThanTheBox()
    {
        using var font = new SKFont(ShareCardFonts.ExtraBold, 56);

        var lines = ShareImageService.WrapText(new string('Ş', 80), font, 1080, 10, out var truncated);

        Assert.False(truncated);
        Assert.True(lines.Count > 1);
        Assert.All(lines, line => Assert.True(font.MeasureText(line) <= 1080));
        Assert.Equal(80, lines.Sum(l => l.Length));
    }

    [Fact]
    public void WrapText_ShortTitleStaysOnOneLine()
    {
        using var font = new SKFont(ShareCardFonts.ExtraBold, 56);

        var lines = ShareImageService.WrapText("Haklı mıyım?", font, 1080, 3, out var truncated);

        Assert.False(truncated);
        Assert.Equal(["Haklı mıyım?"], lines);
    }

    private static void AssertPng(byte[]? bytes, int width, int height)
    {
        Assert.NotNull(bytes);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
        using var codec = SKCodec.Create(new MemoryStream(bytes));
        Assert.NotNull(codec);
        Assert.Equal(SKEncodedImageFormat.Png, codec.EncodedFormat);
        Assert.Equal(width, codec.Info.Width);
        Assert.Equal(height, codec.Info.Height);
    }

    private static int CountNearWhite(SKBitmap bitmap, SKRectI area)
    {
        var count = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Red > 230 && c.Green > 230 && c.Blue > 230)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertColorNear(SKColor actual, SKColor expected)
    {
        Assert.True(
            Math.Abs(actual.Red - expected.Red) <= 3 &&
            Math.Abs(actual.Green - expected.Green) <= 3 &&
            Math.Abs(actual.Blue - expected.Blue) <= 3,
            $"Expected ~{expected} but was {actual}");
    }
}
