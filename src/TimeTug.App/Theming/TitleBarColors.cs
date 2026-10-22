using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace TimeTug.App.Theming;

/// <summary>
/// The colours of a window's caption and its caption buttons for the light and the dark theme. WinUI draws the caption in the system's
/// colours, not the window's theme, so a window forced to the other theme (or an app in dark mode whose caption stayed light) has to be given them.
/// The base is the colour Mica is drawn over (<c>SolidBackgroundFillColorBase</c>) so the caption and the window read as one surface.
/// Under a contrast theme every colour is cleared and Windows draws the caption in the theme's own.
/// </summary>
internal static class TitleBarColors
{
    /// <summary>Gives <paramref name="bar"/> the colours of the theme, or clears them (<paramref name="contrast"/>). Does nothing where captions cannot be customised.</summary>
    public static void Apply(AppWindowTitleBar bar, bool dark, bool contrast)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        if (contrast)
        {
            bar.BackgroundColor = null;
            bar.ForegroundColor = null;
            bar.InactiveBackgroundColor = null;
            bar.InactiveForegroundColor = null;
            bar.ButtonBackgroundColor = null;
            bar.ButtonForegroundColor = null;
            bar.ButtonHoverBackgroundColor = null;
            bar.ButtonHoverForegroundColor = null;
            bar.ButtonPressedBackgroundColor = null;
            bar.ButtonPressedForegroundColor = null;
            bar.ButtonInactiveBackgroundColor = null;
            bar.ButtonInactiveForegroundColor = null;
            return;
        }

        var surface = ThemeColors.Get("SolidBackgroundFillColorBase", dark);
        var text = dark ? Rgb(255, 255, 255) : Rgb(26, 26, 26);
        var quiet = dark ? Rgb(157, 157, 157) : Rgb(110, 110, 110);
        var hover = dark ? Rgb(52, 52, 52) : Rgb(229, 229, 229);
        var pressed = dark ? Rgb(45, 45, 45) : Rgb(218, 218, 218);
        bar.BackgroundColor = surface;
        bar.ForegroundColor = text;
        bar.InactiveBackgroundColor = surface;
        bar.InactiveForegroundColor = quiet;
        bar.ButtonBackgroundColor = surface;
        bar.ButtonForegroundColor = text;
        bar.ButtonHoverBackgroundColor = hover;
        bar.ButtonHoverForegroundColor = text;
        bar.ButtonPressedBackgroundColor = pressed;
        bar.ButtonPressedForegroundColor = text;
        bar.ButtonInactiveBackgroundColor = surface;
        bar.ButtonInactiveForegroundColor = quiet;
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}
