using System.Reflection;
using System.Text.Json;
using FluentAssertions;

namespace Karar.UnitTests.Security;

/// Behavioural tests for the crawler Open Graph page (GET /posts/{id|slug} with a bot User-Agent).
///
/// BuildOgHtml is a static local function in Program.cs top-level statements, so it is invoked
/// through reflection on the compiler-generated method (name contains "g__BuildOgHtml|").
/// The post title is attacker-controlled UGC and must never be able to close the JSON-LD
/// script block or produce invalid JSON-LD.
public sealed class CrawlerOgHtmlInjectionTests
{
    private const string ScriptBreakoutTitle =
        "Kim haklı?</script><meta http-equiv=refresh content='0;url=https://attacker.example/'>";

    [Fact]
    public void BuildOgHtml_TitleCannotCloseJsonLdScriptBlock()
    {
        var html = BuildOgHtml(ScriptBreakoutTitle);

        CountOccurrences(html, "</script", StringComparison.OrdinalIgnoreCase).Should().Be(1,
            "only the legitimate JSON-LD closing tag may appear; the title must not terminate the script element");
        html.Should().NotContain("attacker.example/'>",
            "the raw injected markup must not reach the HTML document");
        html.Should().NotContain("<meta http-equiv=refresh",
            "an injected meta refresh would win over the legitimate one");
    }

    [Theory]
    [InlineData(ScriptBreakoutTitle)]
    [InlineData("Satır\nsonu ve \u0001 kontrol \"tırnak\" \\ ters bölü")]
    [InlineData("Kim haklı? \u2028 ayraç <!-- yorum & 'tek'")]
    public void BuildOgHtml_JsonLdIsValidJsonAndRoundTripsTitle(string title)
    {
        var html = BuildOgHtml(title);

        var json = ExtractJsonLd(html);
        using var document = JsonDocument.Parse(json);

        document.RootElement.GetProperty("@context").GetString().Should().Be("https://schema.org");
        document.RootElement.GetProperty("@type").GetString().Should().Be("Question");
        document.RootElement.GetProperty("name").GetString().Should().Be(title);
        document.RootElement.GetProperty("answerCount").GetInt32().Should().Be(12);
        document.RootElement.GetProperty("datePublished").GetString().Should().Be("2026-10-27");
        document.RootElement.GetProperty("author").GetProperty("name").GetString().Should().Be("Anonim");

        json.Should().NotContain("<", "HTML-sensitive characters must be \\u-escaped inside the script block");
    }

    [Fact]
    public void BuildOgHtml_HtmlAttributesStayEncoded()
    {
        var html = BuildOgHtml(ScriptBreakoutTitle);

        html.Should().Contain("<title>Kim haklı?&lt;/script&gt;");
        html.Should().Contain($"<meta http-equiv=\"refresh\" content=\"0;url=https://karar.app/posts/abc12345-kim-hakli\">");
    }

    private static string BuildOgHtml(string title)
    {
        var method = typeof(Program)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name.Contains("g__BuildOgHtml|", StringComparison.Ordinal));

        return (string)method.Invoke(null, new object?[]
        {
            Guid.Parse("abc12345-0000-0000-0000-000000000000"),
            "abc12345-kim-hakli",
            title,
            "İçerik metni",
            7,
            5,
            null,
            "https://karar.app",
            new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        })!;
    }

    private static string ExtractJsonLd(string html)
    {
        const string open = "<script type=\"application/ld+json\">";
        var start = html.IndexOf(open, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        start += open.Length;
        var end = html.IndexOf("</script", start, StringComparison.OrdinalIgnoreCase);
        end.Should().BeGreaterThan(start);
        return html[start..end].Trim();
    }

    private static int CountOccurrences(string text, string value, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, comparison)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
