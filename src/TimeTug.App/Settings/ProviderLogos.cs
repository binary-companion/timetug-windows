using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.Presentation.Accounts;

namespace TimeTug.App.Settings;

/// <summary>
/// The vendor logos of <c>Assets/Providers</c> as image sources (spec D19, A1.5). Each SVG is the unmodified vendor mark on a white tile, so one asset serves light,
/// dark and high-contrast themes; <c>Assets/Providers/README.md</c> records where each came from and the brand rules it follows. Created on first use, on the UI thread.
/// </summary>
internal static class ProviderLogos
{
    private static readonly Dictionary<ProviderMark, SvgImageSource> Cache = [];

    /// <summary>The image for a mark; null for <see cref="ProviderMark.None"/>.</summary>
    public static ImageSource? Source(ProviderMark mark)
    {
        if (mark == ProviderMark.None)
        {
            return null;
        }

        if (!Cache.TryGetValue(mark, out var source))
        {
            // Drawn at twice the size it is shown at, so it stays sharp at 200 percent scaling.
            source = new SvgImageSource(new Uri(mark == ProviderMark.Microsoft ? "ms-appx:///Assets/Providers/microsoft.svg" : "ms-appx:///Assets/Providers/google.svg"))
            {
                RasterizePixelWidth = 56,
                RasterizePixelHeight = 56,
            };
            Cache[mark] = source;
        }

        return source;
    }

    /// <summary>The icon of a "+" menu entry: the logo, or the kind's generic glyph.</summary>
    public static IconElement Icon(string kindId) => ProviderMarks.Of(kindId) is var mark and not ProviderMark.None
        ? new ImageIcon { Source = Source(mark), Width = 18, Height = 18 }
        : new FontIcon { Glyph = ProviderGlyphs.Of(kindId) };
}
