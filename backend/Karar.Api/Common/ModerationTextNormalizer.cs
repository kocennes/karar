using System.Globalization;
using System.Text;

namespace Karar.Api.Common;

public static class ModerationTextNormalizer
{
    private static readonly Dictionary<char, char> Homoglyphs = new()
    {
        ['а'] = 'a', ['А'] = 'A',
        ['е'] = 'e', ['Е'] = 'E',
        ['о'] = 'o', ['О'] = 'O',
        ['р'] = 'p', ['Р'] = 'P',
        ['с'] = 'c', ['С'] = 'C',
        ['у'] = 'y', ['У'] = 'Y',
        ['х'] = 'x', ['Х'] = 'X',
        ['і'] = 'i', ['І'] = 'I',
        ['ı'] = 'i', ['İ'] = 'I',
        ['|'] = 'l',
        ['1'] = 'l',
        ['0'] = 'o',
        ['@'] = 'a',
        ['$'] = 's',
        ['3'] = 'e',
        ['7'] = 't',
        ['5'] = 's'
    };

    public static string NormalizeForModeration(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        var normalized = input.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);

        foreach (var rune in normalized.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Format
                or UnicodeCategory.Control
                or UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark)
            {
                continue;
            }

            if (rune.Value <= char.MaxValue && Homoglyphs.TryGetValue((char)rune.Value, out var mapped))
            {
                builder.Append(mapped);
            }
            else
            {
                builder.Append(rune.ToString());
            }
        }

        return builder
            .ToString()
            .Replace("vv", "w", StringComparison.OrdinalIgnoreCase)
            .Normalize(NormalizationForm.FormC)
            .Trim();
    }
}
