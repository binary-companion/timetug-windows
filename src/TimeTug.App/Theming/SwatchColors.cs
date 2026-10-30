using Microsoft.UI.Xaml.Media;
using TimeTug.Presentation.Theming;
using Windows.UI;

namespace TimeTug.App.Theming;

/// <summary>
/// Where a calendar's colour is drawn as a swatch or a bar (the Settings check box, the flyout card's colour bar): the colours it needs against
/// the card it sits on in the light or the dark theme. The decisions (the mark's colour, whether the calendar's own colour is a visible edge) are
/// <see cref="CalendarSwatch"/>'s; this only supplies WinUI's own card and text colours for the theme and turns the result into brushes.
/// A contrast theme never gets here: it draws with the system colours and ignores the calendar's colour.
/// </summary>
internal static class SwatchColors
{
    /// <summary>The swatch of the calendar coloured <paramref name="hex"/> on a card in the given theme.</summary>
    public static CalendarSwatch For(string? hex, bool dark)
    {
        var surface = Surface(dark);
        var text = ThemeColors.Get("TextFillColorSecondary", dark);
        var foreground = Rgb(text).Over(surface, text.A);
        return CalendarSwatch.For(hex, surface, foreground);
    }

    /// <summary>What a card is, opaque, in the theme: WinUI's translucent card fill over the window's base colour.</summary>
    public static Rgb Surface(bool dark)
    {
        var page = ThemeColors.Get("SolidBackgroundFillColorBase", dark);
        var card = ThemeColors.Get("CardBackgroundFillColorDefault", dark);
        return Rgb(card).Over(Rgb(page), card.A);
    }

    public static SolidColorBrush Brush(Rgb colour) => new(Color.FromArgb(255, colour.R, colour.G, colour.B));

    private static Rgb Rgb(Color c) => new(c.R, c.G, c.B);
}
