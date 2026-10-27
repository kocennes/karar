using System.Reflection;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

/// <summary>
/// Regression: "Liste dışı" (unlisted) posts promise "ana akışta görünmez, sadece paylaştığın linkle açılabilir".
/// Feed/search filtered p.is_unlisted = FALSE, but the public sitemap, city trending and public profile
/// listings did not, and the crawler OG page let search engines index them.
/// </summary>
public sealed class UnlistedPostExposureTests
{
    private static string ProgramText => TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void SitemapIndex_CountsOnlyListedPosts()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/sitemap.xml\"",
            "app.MapGet(\"/sitemap-main.xml\"");

        block.Should().Contain("WHERE status = 'active' AND is_unlisted = FALSE",
            "the sitemap page count must not be driven by unlisted posts");
    }

    [Fact]
    public void SitemapPosts_ExcludesUnlistedPosts()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/sitemap-posts-{page:int}.xml\"",
            "app.MapGet(\"/posts/{id:guid}\"");

        block.Should().Contain("WHERE status = 'active' AND is_unlisted = FALSE",
            "unlisted post slugs must never be published to crawlers");
    }

    [Theory]
    [InlineData("app.MapGet(\"/posts/{id:guid}\"", "app.MapGet(\"/posts/{slug}\"")]
    [InlineData("app.MapGet(\"/posts/{slug}\"", "app.MapGet(\"/api/v1/posts/{id:guid}/share-image.png\"")]
    public void CrawlerPages_PassUnlistedFlagToOgHtml(string startMarker, string endMarker)
    {
        var block = SliceBlock(ProgramText, startMarker, endMarker);

        block.Should().Contain("is_unlisted",
            "the crawler page must know whether the post is unlisted");
        block.Should().Contain("createdAt, isUnlisted)",
            "BuildOgHtml must receive the unlisted flag so it can emit noindex");
    }

    [Fact]
    public void OgHtml_ForUnlistedPost_TellsSearchEnginesNotToIndex()
    {
        var html = InvokeBuildOgHtml(isUnlisted: true);

        html.Should().Contain("<meta name=\"robots\" content=\"noindex\">",
            "link previews may still render, but search engines must not index link-only posts");
    }

    [Fact]
    public void OgHtml_ForListedPost_StaysIndexable()
    {
        var html = InvokeBuildOgHtml(isUnlisted: false);

        html.Should().NotContain("noindex");
        html.Should().Contain("<meta property=\"og:title\"");
    }

    [Fact]
    public void CityTrending_ExcludesUnlistedPosts()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/posts/trending/city\"",
            "app.MapPost(\"/api/v1/posts\", async (");

        block.Should().Contain("p.is_unlisted = FALSE",
            "votes put unlisted posts into the Redis city-trending set; the public list must filter them");
    }

    [Fact]
    public void PublicUserPosts_ExcludesUnlistedPostsFromListAndCount()
    {
        var block = SliceBlock(
            ProgramText,
            "app.MapGet(\"/api/v1/users/{username}/posts\", async (",
            "app.MapGet(\"/api/v1/users/me/comments\", async (");

        block.Should().Contain("WHERE user_id = @ownerId AND status = 'active' AND is_unlisted = FALSE",
            "the public profile post count must not reveal unlisted posts");
        block.Should().Contain("p.is_unlisted = FALSE",
            "a public profile must not list link-only posts");
    }

    // BuildOgHtml is a static local function of the top-level Program; invoke the compiled method directly.
    private static string InvokeBuildOgHtml(bool isUnlisted)
    {
        var method = typeof(Program)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name.Contains("g__BuildOgHtml|", StringComparison.Ordinal));

        method.GetParameters().Should().HaveCount(10, "BuildOgHtml must take the unlisted flag");

        return (string)method.Invoke(null, new object?[]
        {
            Guid.Parse("4b8f2f5e-0c1d-4b8e-9f7a-1c2d3e4f5a6b"),
            "4b8f2f5e-bir-karar-basligi",
            "Bir karar başlığı",
            "Gönderi içeriği burada yer alıyor ve en az elli karakter uzunluğunda olmalı.",
            3,
            2,
            null,
            "https://karar.app",
            DateTimeOffset.Parse("2026-10-01T10:00:00Z"),
            isUnlisted
        })!;
    }

    private static string SliceBlock(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker {startMarker} should exist");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker {endMarker} should exist after {startMarker}");
        return text[start..end];
    }
}
