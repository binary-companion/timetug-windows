using System.Globalization;
using System.Text;

namespace TimeTug.Presentation.Settings;

/// <summary>Case- and diacritic-insensitive text, the way the Mac folds text for settings search and for sorting calendar titles.</summary>
public static class TextFolding
{
    /// <summary>"Café" and "CAFE" fold to the same text. Letters keep their order and nothing else changes.</summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>Folded titles in the order a person counts: "Team 2" before "Team 10". Equal folded titles fall back to an ordinal comparison, so the order is total.</summary>
    public static IComparer<string> NaturalTitleOrder { get; } = Comparer<string>.Create(CompareNatural);

    private static int CompareNatural(string? left, string? right)
    {
        var a = Fold(left ?? string.Empty);
        var b = Fold(right ?? string.Empty);
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var numberA = a.AsSpan(startA, i - startA).TrimStart('0');
                var numberB = b.AsSpan(startB, j - startB).TrimStart('0');
                var byLength = numberA.Length.CompareTo(numberB.Length);
                if (byLength != 0)
                {
                    return byLength;
                }

                var byDigits = numberA.CompareTo(numberB, StringComparison.Ordinal);
                if (byDigits != 0)
                {
                    return byDigits;
                }
            }
            else
            {
                var byChar = a[i].CompareTo(b[j]);
                if (byChar != 0)
                {
                    return byChar;
                }

                i++;
                j++;
            }
        }

        var byRemainder = (a.Length - i).CompareTo(b.Length - j);
        return byRemainder != 0 ? byRemainder : string.CompareOrdinal(left, right);
    }
}
