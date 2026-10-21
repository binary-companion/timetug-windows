using System.Globalization;

namespace TimeTug.Presentation.Tray;

/// <summary>
/// Shortens text for places with a fixed room (the notification-area tooltip, a menu row) without breaking a character: every cut falls
/// on a text-element boundary (an emoji, a letter with its combining marks, a flag), never between the two UTF-16 units of one
/// character.
/// </summary>
internal static class TextCut
{
    public const string Ellipsis = "…";

    /// <summary>At most <paramref name="maxElements"/> text elements: the text itself, or its first <c>max - 1</c> elements and an ellipsis.</summary>
    public static string Shorten(string text, int maxElements)
    {
        var ends = ElementEnds(text);
        return ends.Count <= maxElements ? text : Take(text, ends, Math.Max(0, maxElements - 1)) + Ellipsis;
    }

    /// <summary>
    /// <paramref name="compose"/> builds the whole text around <paramref name="title"/>. When the result is longer than
    /// <paramref name="maxLength"/> UTF-16 units, only the title is shortened (the rest, the time, stays readable), by whole text
    /// elements, until the composed text fits. A template that is too long even for an empty title is itself cut on an element boundary.
    /// </summary>
    public static string Fit(string title, int maxLength, Func<string, string> compose)
    {
        var whole = compose(title);
        if (whole.Length <= maxLength)
        {
            return whole;
        }

        var ends = ElementEnds(title);
        var keep = ends.Count;
        while (keep > 0)
        {
            // Drop at least as many units as are over, so the loop is short even for a 400-character title.
            var over = compose(Take(title, ends, keep) + Ellipsis).Length - maxLength;
            if (over <= 0)
            {
                return compose(Take(title, ends, keep) + Ellipsis);
            }

            var dropped = 0;
            var next = keep;
            while (next > 0 && dropped < over)
            {
                dropped += ElementLength(ends, next - 1);
                next--;
            }

            keep = next == keep ? keep - 1 : next;
        }

        var bare = compose(Ellipsis);
        if (bare.Length <= maxLength)
        {
            return bare;
        }

        var bareEnds = ElementEnds(bare);
        var fit = bareEnds.Count;
        while (fit > 0 && bareEnds[fit - 1] + Ellipsis.Length > maxLength)
        {
            fit--;
        }

        return Take(bare, bareEnds, fit) + Ellipsis;
    }

    private static List<int> ElementEnds(string text)
    {
        var ends = new List<int>();
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            ends.Add(elements.ElementIndex + ((string)elements.Current).Length);
        }

        return ends;
    }

    private static string Take(string text, List<int> ends, int elements) => elements <= 0 ? string.Empty : text[..ends[elements - 1]];

    private static int ElementLength(List<int> ends, int index) => ends[index] - (index == 0 ? 0 : ends[index - 1]);
}
