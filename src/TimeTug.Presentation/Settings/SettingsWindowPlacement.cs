using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// Where the Settings window goes. A window the person placed before comes back there when that place is still on a connected display; a
/// window from a display that has gone is discarded, and the default is the Mac's size (780 x 560) centred on the primary display's work
/// area. Sizes are in device-independent pixels and scaled by the display the window is on.
/// </summary>
public static class SettingsWindowPlacement
{
    /// <summary>The Mac's ideal size.</summary>
    public static readonly SizeInDips DefaultSize = new(780, 560);

    /// <summary>The Mac's minimum size.</summary>
    public static readonly SizeInDips MinimumSize = new(720, 460);

    /// <summary>The geometry to open at, in physical pixels.</summary>
    public static WindowGeometry Compute(WindowGeometry? remembered, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (remembered is { } kept && kept.ValidFor(monitors))
        {
            var host = MonitorAt(kept, monitors);
            return kept with
            {
                Width = Math.Max(kept.Width, Pixels(MinimumSize.Width, host.Scale)),
                Height = Math.Max(kept.Height, Pixels(MinimumSize.Height, host.Scale)),
            };
        }

        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? (monitors.Count > 0 ? monitors[0] : new MonitorInfo("none", 0, 0, 1920, 1080, true));
        var work = primary.Work;
        var width = Math.Min(Pixels(DefaultSize.Width, primary.Scale), work.Width);
        var height = Math.Min(Pixels(DefaultSize.Height, primary.Scale), work.Height);
        return new WindowGeometry(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height);
    }

    private static int Pixels(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    /// <summary>The display that holds most of the window's title bar.</summary>
    private static MonitorInfo MonitorAt(WindowGeometry geometry, IReadOnlyList<MonitorInfo> monitors)
    {
        MonitorInfo? best = null;
        long bestArea = -1;
        foreach (var monitor in monitors)
        {
            var width = Math.Min(geometry.X + geometry.Width, monitor.Work.Right) - Math.Max(geometry.X, monitor.Work.X);
            var height = Math.Min(geometry.Y + 48, monitor.Work.Bottom) - Math.Max(geometry.Y, monitor.Work.Y);
            var area = width > 0 && height > 0 ? (long)width * height : 0;
            if (area > bestArea)
            {
                bestArea = area;
                best = monitor;
            }
        }

        return best ?? monitors[0];
    }
}
