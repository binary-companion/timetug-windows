using Microsoft.UI.Xaml.Media;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Tug;
using Windows.UI;

namespace TimeTug.App.Tug;

/// <summary>
/// The brushes one tug window uses. The brand palette is the navy design of the Mac overlay; in a Windows contrast theme every
/// role is replaced by the theme's own colours (window, text, highlight), which the user chose and which always have enough contrast.
/// </summary>
internal sealed class TugTheme
{
    private readonly Func<TugTone, TugColor?> _headline;

    private TugTheme(TugColor windowColor, Brush window, Brush text, Brush muted, Brush hint, Brush accent, Brush onAccent, Func<TugTone, TugColor?> headline, bool isContrast)
    {
        WindowIsDark = (0.2126 * windowColor.R) + (0.7152 * windowColor.G) + (0.0722 * windowColor.B) < 128;
        Window = window;
        Text = text;
        Muted = muted;
        Hint = hint;
        Accent = accent;
        OnAccent = onAccent;
        _headline = headline;
        IsContrast = isContrast;
        HeadlineBrushes = Enum.GetValues<TugTone>().ToDictionary(tone => tone, tone => _headline(tone) is { } c ? Solid(c) : text);
    }

    /// <summary>True when the window colour is dark, so the controls' own text defaults to light.</summary>
    public bool WindowIsDark { get; }

    public Brush Window { get; }

    public Brush Text { get; }

    public Brush Muted { get; }

    public Brush Hint { get; }

    /// <summary>The primary button's fill.</summary>
    public Brush Accent { get; }

    public Brush OnAccent { get; }

    public bool IsContrast { get; }

    public IReadOnlyDictionary<TugTone, Brush> HeadlineBrushes { get; }

    public static TugTheme For(TugPalette palette)
    {
        if (palette == TugPalette.SystemContrast)
        {
            Brush Role(SystemColorRole role)
            {
                var (r, g, b) = TugWindowChrome.SystemColor(role);
                return Solid(new TugColor(r, g, b));
            }

            var (wr, wg, wb) = TugWindowChrome.SystemColor(SystemColorRole.Window);
            // Everything the tug says is information the person needs, so it is all WindowText: GrayText means "unavailable" in a
            // contrast theme and some themes give it too little contrast to read.
            var text = Role(SystemColorRole.WindowText);
            return new TugTheme(new TugColor(wr, wg, wb), Role(SystemColorRole.Window), text, text, text, Role(SystemColorRole.Highlight), Role(SystemColorRole.HighlightText), _ => null, isContrast: true);
        }

        return new TugTheme(TugBrand.Navy, Solid(TugBrand.Navy), Solid(new TugColor(255, 255, 255)), Solid(TugBrand.Muted), Solid(TugBrand.Hint), Solid(TugBrand.Blue), Solid(TugBrand.OnBlue), tone => TugBrand.Headline(tone), isContrast: false);
    }

    public static SolidColorBrush Solid(TugColor color, byte alpha = 255) => new(Color.FromArgb(alpha, color.R, color.G, color.B));
}
