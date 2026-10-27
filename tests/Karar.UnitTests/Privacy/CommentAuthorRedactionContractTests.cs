using System.Text.RegularExpressions;
using FluentAssertions;

namespace Karar.UnitTests.Privacy;

// Anonim bir postta yazarın kendi yorumu ("Yargılanan" rozeti) yorum listesinde
// CASE WHEN p.is_anonymous ... redaksiyonuyla isimsiz döner. Aynı yorumu dönen başka
// bir projeksiyon (tepki uç noktalarının kullandığı ReadCommentDtoAsync) bu redaksiyonu
// atlarsa, tek bir yan etkisiz DELETE /api/v1/comments/{id}/reactions isteği OP'nin
// gerçek kullanıcı adını ve userId'sini sızdırır. SQL Postgres olmadan çalıştırılamadığı
// için bu sözleşme kaynak metin üzerinden doğrulanır.
public sealed class CommentAuthorRedactionContractTests
{
    private const string RedactedUsername =
        "CASE WHEN p.is_anonymous = TRUE AND cm.user_id = p.user_id THEN NULL ELSE u.username END";

    private const string RedactedAuthorId =
        "CASE WHEN p.is_anonymous = TRUE AND cm.user_id = p.user_id THEN NULL ELSE cm.user_id END";

    private const string NullSafeOwnership =
        "COALESCE((CASE WHEN cm.user_id IS NULL THEN cm.device_id = @deviceId ELSE cm.user_id = @userId END), FALSE)";

    private const string CommentAuthorJoin = "LEFT JOIN users u ON u.id = cm.user_id";

    private static readonly string ProgramText =
        TestRepoPaths.ReadText("backend", "Karar.Api", "Program.cs");

    [Fact]
    public void ReadCommentDtoAsync_RedactsAnonymousPostOwnerIdentity()
    {
        var block = ReadCommentDtoBlock();

        block.Should().Contain(RedactedUsername,
            "reaction responses must not reveal the username of an anonymous post's author");
        block.Should().Contain(RedactedAuthorId,
            "reaction responses must not reveal the userId of an anonymous post's author");
        block.Should().NotContain("cm.is_pinned, u.username, cm.user_id",
            "the raw author columns must not be projected next to the anonymity flag");
    }

    [Fact]
    public void CommentReactionEndpoints_BuildResponsesThroughRedactingProjection()
    {
        var post = SliceBlock(
            ProgramText,
            "app.MapPost(\"/api/v1/comments/{id:guid}/reactions\"",
            "app.MapDelete(\"/api/v1/comments/{id:guid}/reactions\"");
        var delete = SliceBlock(
            ProgramText,
            "app.MapDelete(\"/api/v1/comments/{id:guid}/reactions\"",
            "app.MapPost(\"/api/v1/reports\"");

        post.Should().Contain("ReadCommentDtoAsync(connection, id, deviceId.Value, userId)");
        delete.Should().Contain("ReadCommentDtoAsync(connection, id, deviceId.Value, userId)");
        post.Should().NotContain("u.username");
        delete.Should().NotContain("u.username");
    }

    [Fact]
    public void EveryCommentAuthorJoin_ProjectsAuthorOnlyThroughAnonymityCase()
    {
        var joinIndexes = AllIndexesOf(ProgramText, CommentAuthorJoin);
        joinIndexes.Should().NotBeEmpty("comment queries join the author for display");

        foreach (var joinIndex in joinIndexes)
        {
            var region = EnclosingRegion(ProgramText, joinIndex);

            foreach (var usernameIndex in AllIndexesOf(region, "u.username"))
            {
                var prefixStart = Math.Max(0, usernameIndex - "THEN NULL ELSE ".Length);
                region[prefixStart..usernameIndex].Should().Be("THEN NULL ELSE ",
                    "every username projected next to a comment must go through the anonymity CASE (region starting: {0})",
                    region[..Math.Min(80, region.Length)]);
            }

            Regex.IsMatch(region, @",\s*cm\.user_id\s*,").Should().BeFalse(
                "a bare cm.user_id projection exposes the anonymous author's id (region starting: {0})",
                region[..Math.Min(80, region.Length)]);
        }
    }

    [Fact]
    public void CommentOwnershipFlag_IsNullSafeForGuestsAndGuestComments()
    {
        // Oturumsuz çağıranda @userId NULL, misafir yorumda cm.user_id NULL olur;
        // FALSE OR NULL = NULL → GetBoolean(6) InvalidCastException → 500.
        ReadCommentDtoBlock().Should().Contain(NullSafeOwnership);

        var listBlock = SliceBlock(
            ProgramText,
            "const string commentSelectColumns = \"\"\"",
            "static CommentDto ReadCommentRow(NpgsqlDataReader r)");
        listBlock.Should().Contain(NullSafeOwnership);
    }

    private static string ReadCommentDtoBlock() =>
        SliceBlock(
            ProgramText,
            "static async Task<CommentDto?> ReadCommentDtoAsync(",
            "static async Task<IResult> SetCommentUpvoteAsync(");

    // Bir endpoint lambda'sı ya da yerel static fonksiyon sınırları içinde kalan metin.
    private static string EnclosingRegion(string text, int index)
    {
        var markers = new[] { "\napp.Map", "\nstatic async ", "\nstatic " };
        var start = markers
            .Select(m => text.LastIndexOf(m, index, StringComparison.Ordinal))
            .Max();
        var end = markers
            .Select(m => text.IndexOf(m, index, StringComparison.Ordinal))
            .Where(i => i >= 0)
            .DefaultIfEmpty(text.Length)
            .Min();
        return text[Math.Max(0, start)..end];
    }

    private static List<int> AllIndexesOf(string text, string pattern)
    {
        var result = new List<int>();
        var index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) >= 0)
        {
            result.Add(index);
            index += pattern.Length;
        }

        return result;
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
