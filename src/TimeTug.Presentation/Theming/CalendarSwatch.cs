namespace TimeTug.Presentation.Theming;

/// <summary>An opaque sRGB colour.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb White { get; } = new(255, 255, 255);

    public static Rgb Black { get; } = new(0, 0, 0);

    /// <summary>Reads "#RRGGBB" (the only form the Mac accepts either); anything else is not a colour.</summary>
    public static bool TryParseHex(string? hex, out Rgb colour)
    {
        colour = default;
        if (hex is not { Length: 7 } || hex[0] != '#' || !hex.Skip(1).All(Uri.IsHexDigit))
        {
            return false;
        }

        colour = new Rgb(Channel(hex, 1), Channel(hex, 3), Channel(hex, 5));
        return true;
    }

    /// <summary>"#RRGGBB".</summary>
    public string ToHex() => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>This colour drawn with <paramref name="alpha"/> (0 to 255) over an opaque <paramref name="background"/>.</summary>
    public Rgb Over(Rgb background, byte alpha) => new(Mix(R, background.R, alpha), Mix(G, background.G, alpha), Mix(B, background.B, alpha));

    private static byte Channel(string hex, int at) => byte.Parse(hex.AsSpan(at, 2), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture);

    private static byte Mix(byte top, byte below, byte alpha) => (byte)Math.Round((top * alpha + below * (255 - alpha)) / 255.0, MidpointRounding.AwayFromZero);
}

/// <summary>Relative luminance and contrast ratio as WCAG 2 defines them.</summary>
public static class ColorContrast
{
    /// <summary>Text, and so a check mark: 4.5 to 1.</summary>
    public const double TextMinimum = 4.5;

    /// <summary>A graphical object such as the edge of a check box: 3 to 1.</summary>
    public const double GraphicMinimum = 3.0;

    /// <summary>
    /// A colour bar or swatch that only decorates (the calendar's stripe on a card): it is left as it is while it differs from what is behind it
    /// at all clearly, 1.5 to 1 (pale yellow on white is 1.1, the dark card's own grey 1.0). The 3 to 1 of <see cref="GraphicMinimum"/> would put
    /// an edge round Google's orange and green on a white card, which the Mac does not do.
    /// </summary>
    public const double BarMinimum = 1.5;

    private static readonly Rgb NearBlack = new(0x10, 0x10, 0x10);

    public static double RelativeLuminance(Rgb colour) => 0.2126 * Linear(colour.R) + 0.7152 * Linear(colour.G) + 0.0722 * Linear(colour.B);

    /// <summary>(L1 + 0.05) / (L2 + 0.05) with L1 the lighter of the two: 1 to 21, whichever order the colours are given in.</summary>
    public static double Ratio(Rgb a, Rgb b)
    {
        var (la, lb) = (RelativeLuminance(a), RelativeLuminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// The colour to draw on <paramref name="fill"/> that reads at 4.5 to 1 or better: white while it does, else near-black (#101010), else
    /// black. Black always does where white does not (white falls under 4.5 only above a luminance of 0.183, and black is 4.67 to 1 there).
    /// </summary>
    public static Rgb ReadableOn(Rgb fill)
    {
        if (Ratio(Rgb.White, fill) >= TextMinimum)
        {
            return Rgb.White;
        }

        return Ratio(NearBlack, fill) >= TextMinimum ? NearBlack : Rgb.Black;
    }

    private static double Linear(byte channel)
    {
        var c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }
}

/// <summary>
/// How a calendar's colour is drawn where it is a swatch or a bar: the fill, the mark on it, and the edge. Everything is decided from the
/// colours alone, so it can be tested: the mark reads on the fill at 4.5 to 1, and the edge reads on the surface the swatch sits on at 3 to 1
/// (the calendar's own colour when it already does, the theme's foreground when it does not). <c>FillShows</c> says whether the colour differs from the
/// surface clearly enough (1.5 to 1) to stand as a bare bar. A missing or malformed colour draws in the foreground.
/// </summary>
public readonly record struct CalendarSwatch(Rgb Fill, Rgb Mark, Rgb Edge, bool UsesCalendarColor, bool FillShows)
{
    /// <param name="hex">The calendar's colour, "#RRGGBB", or null.</param>
    /// <param name="surface">What the swatch sits on, opaque.</param>
    /// <param name="foreground">The theme's own text colour on that surface, opaque.</param>
    public static CalendarSwatch For(string? hex, Rgb surface, Rgb foreground)
    {
        if (!Rgb.TryParseHex(hex, out var fill))
        {
            return new CalendarSwatch(foreground, ColorContrast.ReadableOn(foreground), foreground, UsesCalendarColor: false, FillShows: true);
        }

        var edge = ColorContrast.Ratio(fill, surface) >= ColorContrast.GraphicMinimum ? fill : foreground;
        return new CalendarSwatch(fill, ColorContrast.ReadableOn(fill), edge, UsesCalendarColor: true, FillShows: ColorContrast.Ratio(fill, surface) >= ColorContrast.BarMinimum);
    }
}
