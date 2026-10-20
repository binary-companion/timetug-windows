using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TimeTug.Platform.Tug;
using Windows.UI;

namespace TimeTug.App.Flyout;

/// <summary>
/// The brushes of the flyout. The text, card and border brushes are Windows' own theme brushes (they follow light, dark and every
/// contrast theme); the blue, the pills and the progress bar are the Mac popup's brand colours (<c>PopupPalette</c>), and in a
/// contrast theme every one of them is replaced by the theme's own Highlight and Window colours.
/// </summary>
internal sealed class FlyoutPalette
{
    private FlyoutPalette()
    {
    }

    public bool IsContrast { get; private init; }

    public Brush Primary { get; private init; } = null!;

    public Brush Secondary { get; private init; } = null!;

    public Brush CardFill { get; private init; } = null!;

    public Brush CardStroke { get; private init; } = null!;

    /// <summary>The accent: the progress bar, the border of the next meeting, the default bar colour of a calendar without one.</summary>
    public Brush Blue { get; private init; } = null!;

    public Brush PillBackground { get; private init; } = null!;

    public Brush PillText { get; private init; } = null!;

    /// <summary>The countdown pill on the next meeting (amber on the Mac).</summary>
    public Brush ChipBackground { get; private init; } = null!;

    public Brush ChipText { get; private init; } = null!;

    public Brush ProgressTrack { get; private init; } = null!;

    public Brush ProblemFill { get; private init; } = null!;

    public Brush ProblemIcon { get; private init; } = null!;

    public Brush ProblemStroke { get; private init; } = null!;

    public static FlyoutPalette For(bool dark, bool contrast)
    {
        if (contrast)
        {
            Brush Role(SystemColorRole role)
            {
                var (r, g, b) = TugWindowChrome.SystemColor(role);
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }

            var text = Role(SystemColorRole.WindowText);
            var window = Role(SystemColorRole.Window);
            var highlight = Role(SystemColorRole.Highlight);
            var onHighlight = Role(SystemColorRole.HighlightText);
            return new FlyoutPalette
            {
                IsContrast = true,
                Primary = text,
                Secondary = text,
                CardFill = window,
                CardStroke = text,
                Blue = highlight,
                PillBackground = highlight,
                PillText = onHighlight,
                ChipBackground = highlight,
                ChipText = onHighlight,
                ProgressTrack = window,
                ProblemFill = window,
                ProblemIcon = text,
                ProblemStroke = text,
            };
        }

        return new FlyoutPalette
        {
            Primary = Theme("TextFillColorPrimaryBrush", dark ? Rgb(255, 255, 255) : Rgb(26, 26, 26)),
            Secondary = Theme("TextFillColorSecondaryBrush", dark ? Rgb(200, 200, 200) : Rgb(96, 96, 96)),
            CardFill = Theme("CardBackgroundFillColorDefaultBrush", dark ? Rgb(60, 60, 60) : Rgb(255, 255, 255)),
            CardStroke = Theme("CardStrokeColorDefaultBrush", dark ? Rgb(70, 70, 70) : Rgb(229, 229, 229)),
            Blue = dark ? Rgb(77, 148, 255) : Rgb(46, 122, 245),
            PillBackground = dark ? Rgb(31, 54, 102) : Rgb(222, 235, 255),
            PillText = dark ? Rgb(191, 217, 255) : Rgb(26, 87, 184),
            ChipBackground = dark ? Rgb(74, 51, 15) : Rgb(255, 231, 194),
            ChipText = dark ? Rgb(255, 209, 138) : Rgb(138, 74, 0),
            ProgressTrack = dark ? Rgb(42, 54, 88) : Rgb(231, 220, 203),
            ProblemFill = new SolidColorBrush(Color.FromArgb(41, 255, 149, 0)),
            ProblemIcon = Rgb(255, 149, 0),
            ProblemStroke = new SolidColorBrush(Color.FromArgb(77, 255, 149, 0)),
        };
    }

    /// <summary>"#RRGGBB" as a brush; null for anything else (the Mac accepts nothing but that form either).</summary>
    public static Brush? FromHex(string? hex)
    {
        if (hex is not { Length: 7 } || hex[0] != '#' || !int.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Rgb((byte)((value >> 16) & 0xFF), (byte)((value >> 8) & 0xFF), (byte)(value & 0xFF));
    }

    private static SolidColorBrush Rgb(byte r, byte g, byte b) => new(Color.FromArgb(255, r, g, b));

    private static Brush Theme(string key, Brush fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : fallback;
}
