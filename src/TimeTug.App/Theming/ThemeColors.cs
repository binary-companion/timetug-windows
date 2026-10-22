using Microsoft.UI.Xaml;
using Windows.UI;

namespace TimeTug.App.Theming;

/// <summary>
/// WinUI's own theme colours for a theme chosen in code. A brush found by key in <c>Application.Resources</c> is the one for the theme the
/// app started in, which is wrong for a window the person forced to the other theme, so code that draws with brushes asks here for the
/// colour of the theme it means. The colour comes from the theme dictionaries when WinUI exposes them, and from the values WinUI ships otherwise.
/// </summary>
internal static class ThemeColors
{
    /// <summary>The colours WinUI 3 defines for the keys the app draws with (<c>Common_themeresources_any.xaml</c>), as light and dark pairs.</summary>
    private static readonly Dictionary<string, (Color Light, Color Dark)> Known = new(StringComparer.Ordinal)
    {
        ["TextFillColorPrimary"] = (Color.FromArgb(0xE4, 0, 0, 0), Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)),
        ["TextFillColorSecondary"] = (Color.FromArgb(0x9E, 0, 0, 0), Color.FromArgb(0xC5, 0xFF, 0xFF, 0xFF)),
        ["CardBackgroundFillColorDefault"] = (Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF)),
        ["CardStrokeColorDefault"] = (Color.FromArgb(0x0F, 0, 0, 0), Color.FromArgb(0x19, 0, 0, 0)),
        ["SolidBackgroundFillColorBase"] = (Color.FromArgb(0xFF, 0xF3, 0xF3, 0xF3), Color.FromArgb(0xFF, 0x20, 0x20, 0x20)),
    };

    /// <summary>The colour called <paramref name="key"/> (without "Brush") in the light or the dark theme.</summary>
    public static Color Get(string key, bool dark)
    {
        if (FromDictionaries(key, dark) is { } found)
        {
            return found;
        }

        return Known.TryGetValue(key, out var pair) ? (dark ? pair.Dark : pair.Light) : throw new KeyNotFoundException($"no theme colour {key}");
    }

    /// <summary>True when WinUI's theme dictionaries gave the colour (false: the built-in value was used). For the self-test's notes.</summary>
    internal static bool InDictionaries(string key, bool dark) => FromDictionaries(key, dark) is not null;

    private static Color? FromDictionaries(string key, bool dark)
    {
        try
        {
            return Search(Application.Current.Resources, key, dark ? "Default" : "Light");
        }
        catch (Exception e) when (e is InvalidCastException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    private static Color? Search(ResourceDictionary dictionary, string key, string theme)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out var themed) && themed is ResourceDictionary scoped
            && scoped.TryGetValue(key, out var value) && value is Color color)
        {
            return color;
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (Search(merged, key, theme) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
