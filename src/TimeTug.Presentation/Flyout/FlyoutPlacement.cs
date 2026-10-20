using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Flyout;

/// <summary>
/// Where the flyout goes. A pure function in physical pixels, because the window layer places windows with
/// <c>AppWindow.MoveAndResize</c>, which takes pixels: the flyout is 360 DIP wide and as tall as its content up to 520 DIP, scaled by the
/// DPI of the monitor that holds the tray icon, with an 8 DIP gap from the taskbar and from the screen's edge.
/// <para>
/// The taskbar's edge is read from the monitor: the side where the work area is smaller than the display (a taskbar that hides itself
/// reserves nothing, so then the display edge nearest the icon is used). The flyout opens above a bottom taskbar, below a top one and
/// beside a side one, centred on the icon along the taskbar, then slid into the work area.
/// </para>
/// </summary>
public static class FlyoutPlacement
{
    public const double WidthDips = 360;

    public const double MaxHeightDips = 520;

    public const double MarginDips = 8;

    /// <summary>
    /// The window rectangle for the flyout. <paramref name="anchor"/> is the tray icon (or its stand-in); without one the flyout
    /// goes to the bottom-right corner of the primary monitor's work area. Only the height of <paramref name="content"/> matters:
    /// the width is always 360 DIP. Never throws and never returns a rectangle outside a monitor's work area (smaller than the
    /// flyout, it shrinks).
    /// </summary>
    public static PixelRect Compute(PixelRect? anchor, SizeInDips content, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return new PixelRect(0, 0, (int)WidthDips, (int)Math.Min(Math.Max(content.Height, 1), MaxHeightDips));
        }

        var target = anchor is { } a ? MonitorFor(a, monitors) : monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var work = target.Work;
        var margin = Pixels(MarginDips, target.Scale);

        // The size: never taller than 520 DIP, never larger than the room there is.
        var height = Math.Min(Pixels(Math.Min(Math.Max(content.Height, 1), MaxHeightDips), target.Scale), Math.Max(1, work.Height - (2 * margin)));
        var width = Math.Min(Pixels(WidthDips, target.Scale), Math.Max(1, work.Width - (2 * margin)));

        int x;
        int y;
        if (anchor is not { } icon)
        {
            x = work.Right - margin - width;
            y = work.Bottom - margin - height;
        }
        else
        {
            switch (TaskbarEdge(target, icon))
            {
                case Edge.Top:
                    y = Math.Max(work.Y, icon.Bottom) + margin;
                    x = Round(icon.CenterX - (width / 2.0));
                    break;
                case Edge.Left:
                    x = Math.Max(work.X, icon.Right) + margin;
                    y = Round(icon.CenterY - (height / 2.0));
                    break;
                case Edge.Right:
                    x = Math.Min(work.Right, icon.X) - margin - width;
                    y = Round(icon.CenterY - (height / 2.0));
                    break;
                default:
                    y = Math.Min(work.Bottom, icon.Y) - margin - height;
                    x = Round(icon.CenterX - (width / 2.0));
                    break;
            }
        }

        return new PixelRect(Clamp(x, work.X + margin, work.Right - margin - width), Clamp(y, work.Y + margin, work.Bottom - margin - height), width, height);
    }

    /// <summary>The monitor that holds the anchor's centre; when the centre is on none (a layout that changed), the nearest one.</summary>
    public static MonitorInfo MonitorFor(PixelRect anchor, IReadOnlyList<MonitorInfo> monitors)
    {
        var (cx, cy) = (anchor.CenterX, anchor.CenterY);
        var inside = monitors.FirstOrDefault(m => m.Bounds.Contains(cx, cy));
        if (inside is not null)
        {
            return inside;
        }

        return monitors.MinBy(m => Distance(m.Bounds, cx, cy))!;
    }

    private enum Edge
    {
        Bottom,
        Top,
        Left,
        Right,
    }

    private static Edge TaskbarEdge(MonitorInfo monitor, PixelRect anchor)
    {
        var bounds = monitor.Bounds;
        var work = monitor.Work;

        // The taskbar takes space from exactly one side; the side that lost the most is where it is.
        var insets = new (Edge Edge, int Amount)[]
        {
            (Edge.Bottom, bounds.Bottom - work.Bottom),
            (Edge.Top, work.Y - bounds.Y),
            (Edge.Left, work.X - bounds.X),
            (Edge.Right, bounds.Right - work.Right),
        };
        var largest = insets.MaxBy(i => i.Amount);
        if (largest.Amount > 0)
        {
            return largest.Edge;
        }

        // A hidden taskbar: the display edge closest to the icon.
        var near = new (Edge Edge, double Distance)[]
        {
            (Edge.Bottom, Math.Abs(bounds.Bottom - anchor.CenterY)),
            (Edge.Top, Math.Abs(anchor.CenterY - bounds.Y)),
            (Edge.Left, Math.Abs(anchor.CenterX - bounds.X)),
            (Edge.Right, Math.Abs(bounds.Right - anchor.CenterX)),
        };
        return near.MinBy(n => n.Distance).Edge;
    }

    private static double Distance(PixelRect rect, double x, double y)
    {
        var dx = Math.Max(Math.Max(rect.X - x, x - rect.Right), 0);
        var dy = Math.Max(Math.Max(rect.Y - y, y - rect.Bottom), 0);
        return (dx * dx) + (dy * dy);
    }

    private static int Pixels(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    // When the room is smaller than the window the top-left wins, so the header is never cut off.
    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Min(Math.Max(value, min), max);
}
