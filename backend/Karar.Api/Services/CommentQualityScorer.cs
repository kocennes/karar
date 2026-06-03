using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Karar.Api.Services;

public static class CommentQualityScorer
{
    private static readonly Regex WordRegex = new(@"\p{L}[\p{L}\p{Mn}']*", RegexOptions.Compiled);
    private static readonly Regex PersonalAttackRegex = new(
        @"\b(salak|aptal|gerizekal\u0131|mal|s\u00fcrt\u00fck|orospu|pi\u00e7|it|k\u00f6pek|e\u015fek)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static float Score(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0f;
        }

        var words = WordRegex.Matches(content);
        var wordCount = words.Count;
        var penalty = 0f;

        if (wordCount < 3)
        {
            penalty += 0.4f;
        }

        if (EmojiRatio(content) > 0.8f)
        {
            penalty += 0.5f;
        }

        var attackMatches = PersonalAttackRegex.Matches(content).Count;
        if (attackMatches > 0)
        {
            penalty += Math.Min(attackMatches * 0.4f, 0.6f);
        }

        if (wordCount > 2 && IsAllCaps(content))
        {
            penalty += 0.2f;
        }

        return Math.Min(penalty, 1.0f);
    }

    private static float EmojiRatio(string content)
    {
        var visibleRunes = 0;
        var emojiRunes = 0;

        foreach (var rune in content.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.SpaceSeparator
                or UnicodeCategory.Control
                or UnicodeCategory.Format
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            visibleRunes++;
            if (category is UnicodeCategory.OtherSymbol
                or UnicodeCategory.ModifierSymbol)
            {
                emojiRunes++;
            }
        }

        return visibleRunes == 0 ? 0f : emojiRunes / (float)visibleRunes;
    }

    private static bool IsAllCaps(string content)
    {
        var hasLetter = false;
        foreach (var rune in content.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune))
            {
                continue;
            }

            hasLetter = true;
            if (Rune.ToUpperInvariant(rune) != rune)
            {
                return false;
            }
        }

        return hasLetter;
    }
}
