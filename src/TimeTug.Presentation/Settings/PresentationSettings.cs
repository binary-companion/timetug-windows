using System.Globalization;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Presentation.Settings;

/// <summary>Which colour theme TimeTug's own windows use. The tug stays navy whatever this says.</summary>
public enum AppTheme
{
    UseSystem,
    Light,
    Dark,
}

/// <summary>What the tray icon draws: the Windows counterpart of the Mac's menu-bar text modes (no title text fits in the tray).</summary>
public enum TrayIconStyle
{
    /// <summary>The puppy alone.</summary>
    IconOnly,

    /// <summary>The puppy, with the remaining whole minutes drawn on it in the last ten minutes.</summary>
    IconWithMinuteDigit,
}

/// <summary>
/// A window's place and size in physical pixels (virtual-screen coordinates; negative values are normal for a monitor left of or above the
/// primary one). Kept between runs; <see cref="ValidFor"/> decides on the next run whether it still lands on a connected display.
/// </summary>
public readonly record struct WindowGeometry(int X, int Y, int Width, int Height)
{
    /// <summary>The smallest piece of the window that must be on a display for it to be reachable: its title bar.</summary>
    private const int ReachableWidth = 120;

    private const int ReachableHeight = 48;

    /// <summary>The geometry is usable on these displays: it has a size and enough of its top edge is inside some display's work area to grab it.</summary>
    public bool ValidFor(IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (Width <= 0 || Height <= 0)
        {
            return false;
        }

        var titleBar = new PixelRect(X, Y, Width, Math.Min(Height, ReachableHeight));
        foreach (var monitor in monitors)
        {
            var work = monitor.Work;
            var width = Math.Min(titleBar.Right, work.Right) - Math.Max(titleBar.X, work.X);
            var height = Math.Min(titleBar.Bottom, work.Bottom) - Math.Max(titleBar.Y, work.Y);
            if (width >= Math.Min(ReachableWidth, Width) && height >= Math.Min(ReachableHeight, titleBar.Height))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>"x,y,width,height" in invariant digits, the form the store keeps.</summary>
    public string ToStorage() => string.Create(CultureInfo.InvariantCulture, $"{X},{Y},{Width},{Height}");

    /// <summary>Reads <see cref="ToStorage"/> back; null for anything else (wrong count, not integers, no area, absurdly large).</summary>
    public static WindowGeometry? FromStorage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(',');
        if (parts.Length != 4)
        {
            return null;
        }

        var numbers = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        const int Limit = 100_000;
        var geometry = new WindowGeometry(numbers[0], numbers[1], numbers[2], numbers[3]);
        var sane = geometry.Width is > 0 and <= Limit && geometry.Height is > 0 and <= Limit
            && Math.Abs(geometry.X) <= Limit && Math.Abs(geometry.Y) <= Limit;
        return sane ? geometry : null;
    }
}

/// <summary>
/// The app's own presentation preferences, kept app-side (not in the engine's <c>settings.json</c>, which is behaviour only). The popup
/// shortcut has its own store; launch at login has none (Windows is the source of truth).
/// </summary>
/// <param name="Theme">Use the system's theme, or force light or dark.</param>
/// <param name="TrayStyle">Icon only, or with the minute digit in the last ten minutes.</param>
/// <param name="Backdrop">The flyout's background: <see cref="FlyoutBackdrop.Material"/> (acrylic) or <see cref="FlyoutBackdrop.Solid"/>. Reduced transparency and contrast themes override it.</param>
/// <param name="SettingsWindow">Where the Settings window was last; null when never moved or resized.</param>
public sealed record PresentationSettings(AppTheme Theme, TrayIconStyle TrayStyle, FlyoutBackdrop Backdrop, WindowGeometry? SettingsWindow)
{
    /// <summary>A fresh install: follow the system theme, show the minute digit, use acrylic, let Windows place the Settings window.</summary>
    public static PresentationSettings Defaults { get; } = new(AppTheme.UseSystem, TrayIconStyle.IconWithMinuteDigit, FlyoutBackdrop.Material, null);
}
