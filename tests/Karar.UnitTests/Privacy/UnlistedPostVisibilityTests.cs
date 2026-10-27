using System.Reflection;
using FluentAssertions;
using Karar.UnitTests;

namespace Karar.UnitTests.Privacy;

/// [F7] "Liste dışı paylaş" (posts.is_unlisted) gönderileri yalnızca linki bilenlerce açılabilmeli.
/// Sitemap, slug tabanlı crawler sayfası, herkese açık profil/yorum listeleri ve şehir trendi bu
/// gönderileri yayınlamamalı; link önizlemesi (id rotası) ise arama motorlarına noindex demeli.
/// SQL sorguları Postgres gerektirdiğinden kaynak sözleşmesiyle, OG HTML üretimi ise davranışsal
/// olarak (Program içindeki BuildOgHtml doğrudan çağrılarak) test edilir.
public sealed class UnlistedPostVisibilityTests
{
    private const string WebBaseUrl = "https://karar.app";
    private const string Slug = "1a2b3c4d-esim-beni-aldatti-hakli-miyim";
    private static readonly Guid PostId = Guid.Parse("1a2b3c4d-1111-2222-3333-444455556666");

    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    // ── Sitemap ────────────────────────────────────────────────────────────

    [Fact]
    public void SitemapIndex_CountsOnlyListedPosts()
    {
        var block = Slice("app.MapGet(\"/sitemap.xml\"", "app.MapGet(\"/sitemap-main.xml\"");

        block.Should().Contain("is_unlisted = FALSE",
            "the sitemap index must not count (and page through) unlisted posts");
    }

    [Fact]
    public void SitemapPostsPage_ListsOnlyListedPosts()
    {
        var block = Slice("app.MapGet(\"/sitemap-posts-{page:int}.xml\"", "app.MapGet(\"/posts/{id:guid}\"");

        block.Should().Contain("is_unlisted = FALSE",
            "sitemap pages publish slugs to every crawler; unlisted posts must never be listed");
    }

    // ── Crawler / OG pages ─────────────────────────────────────────────────

    [Fact]
    public void CrawlerSlugPage_DoesNotServeUnlistedPosts()
    {
        var block = Slice("app.MapGet(\"/posts/{slug}\"", "app.MapGet(\"/api/v1/posts/{id:guid}/share-image.png\"");

        block.Should().Contain("is_unlisted = FALSE",
            "the app shares unlisted posts by id; a slug page would leak title, content and the post id via og:image");
    }

    [Fact]
    public void CrawlerIdPage_PassesUnlistedFlagToOgHtml()
    {
        var block = Slice("app.MapGet(\"/posts/{id:guid}\"", "app.MapGet(\"/posts/{slug}\"");

        block.Should().Contain("is_unlisted",
            "the id-based preview must know whether the post is unlisted");
        block.Should().MatchRegex(@"BuildOgHtml\([^;]*isUnlisted",
            "unlisted previews must be rendered as noindex");
    }

    [Fact]
    public void BuildOgHtml_UnlistedPost_IsNoIndexAndDoesNotExposeSlug()
    {
        var html = InvokeBuildOgHtml(isUnlisted: true);

        html.Should().Contain("<meta name=\"robots\" content=\"noindex, nofollow\">",
            "search engines must not index a post that is only reachable via its shared link");
        html.Should().Contain($"<link rel=\"canonical\" href=\"{WebBaseUrl}/posts/{PostId}\">",
            "the canonical URL of an unlisted post is the id link that was actually shared");
        html.Should().Contain($"<meta property=\"og:url\" content=\"{WebBaseUrl}/posts/{PostId}\">");
        html.Should().NotContain(Slug,
            "the slug of an unlisted post must not be published anywhere");
    }

    [Fact]
    public void BuildOgHtml_ListedPost_StaysIndexableWithSlugCanonical()
    {
        var html = InvokeBuildOgHtml(isUnlisted: false);

        html.Should().NotContain("noindex", "listed posts keep their SEO behaviour");
        html.Should().Contain($"<link rel=\"canonical\" href=\"{WebBaseUrl}/posts/{Slug}\">");
        html.Should().Contain($"<meta property=\"og:url\" content=\"{WebBaseUrl}/posts/{Slug}\">");
    }

    // ── City trending ──────────────────────────────────────────────────────

    [Fact]
    public void CityTrendingEndpoint_ExcludesUnlistedPosts()
    {
        var block = Slice("app.MapGet(\"/api/v1/posts/trending/city\"", "app.MapPost(\"/api/v1/posts\", async (");

        block.Should().Contain("p.is_unlisted = FALSE",
            "city trending is a public list; selecting the flag is not the same as filtering on it");
    }

    [Fact]
    public void VoteEndpoint_DoesNotPushUnlistedPostsIntoCityTrending()
    {
        var voteBlock = Slice("app.MapPost(\"/api/v1/posts/{id:guid}/vote\"", "app.MapDelete(\"/api/v1/posts/{id:guid}/vote\"");
        var contextBlock = Slice(
            "static async Task<(string? OldVote, int OldTotal",
            "static async Task<(int Hakli, int Haksiz)> UpdateVoteCountersReturningAsync");

        contextBlock.Should().Contain("p.is_unlisted",
            "the vote context must carry the unlisted flag of the post");
        voteBlock.Should().MatchRegex(@"if \([^\n]*!postIsUnlisted[^\n]*\)\s*\n\s*_ = redis\.UpdateCityTrendingAsync",
            "a vote on an unlisted post must not add it to the city trending sorted set");
    }

    // ── Public profile lists ───────────────────────────────────────────────

    [Fact]
    public void PublicUserPosts_ExcludeUnlistedPostsFromListAndCount()
    {
        var block = Slice("app.MapGet(\"/api/v1/users/{username}/posts\"", "app.MapGet(\"/api/v1/users/me/comments\"");

        CountOccurrences(block, "is_unlisted = FALSE").Should().BeGreaterThanOrEqualTo(2,
            "both the total count and the page query of the public profile must skip unlisted posts");
    }

    [Fact]
    public void PublicUserComments_ExcludeCommentsOnUnlistedPosts()
    {
        var block = Slice("app.MapGet(\"/api/v1/users/{username}/comments\"", "app.MapGet(\"/api/v1/users/me/karma-history\"");

        CountOccurrences(block, "p.is_unlisted = FALSE").Should().BeGreaterThanOrEqualTo(2,
            "public comment cards expose the post id and title, so comments on unlisted posts must be skipped in count and page");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string InvokeBuildOgHtml(bool isUnlisted)
    {
        var method = typeof(Program)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .SingleOrDefault(m => m.Name.Contains("g__BuildOgHtml|", StringComparison.Ordinal));
        method.Should().NotBeNull("BuildOgHtml must exist as a static local function of Program");

        var parameters = method!.GetParameters();
        parameters.Select(p => p.Name).Should().Contain("isUnlisted",
            "BuildOgHtml must be able to render unlisted posts differently from listed ones");

        var args = parameters.Select(p => p.Name switch
        {
            "id" => (object?)PostId,
            "slug" => Slug,
            "title" => "Eşim beni aldattı, haklı mıyım?",
            "content" => "Uzun bir hikaye...",
            "hakli" => 3,
            "haksiz" => 2,
            "customImageUrl" => null,
            "webBaseUrl" => WebBaseUrl,
            "createdAt" => new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero),
            "isUnlisted" => isUnlisted,
            _ => p.HasDefaultValue ? p.DefaultValue : throw new InvalidOperationException($"Unexpected parameter {p.Name}")
        }).ToArray();

        return (string)method.Invoke(null, args)!;
    }

    private static string Slice(string startMarker, string endMarker)
    {
        var start = ProgramText.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"start marker '{startMarker}' must exist in Program.cs");
        var end = ProgramText.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"end marker '{endMarker}' must appear after '{startMarker}'");
        return ProgramText[start..end];
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
