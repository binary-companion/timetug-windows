using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Presentation.Chip;

/// <summary>
/// Where the chip goes. A pure function in physical pixels (the window layer places windows with <c>AppWindow.MoveAndResize</c>), from the
/// positions the person dragged it to, the connected monitors, the tray icon's place and the chip's size in DIPs.
/// <list type="bullet">
/// <item>A saved position is the chip's top-left in DIPs from its monitor's work-area origin. It is used when that monitor is still connected and
/// the chip would still sit on its work area (its centre is inside it); the newest valid one wins. It is then slid fully into the work area.
/// DIPs from the monitor (not pixels in the virtual screen) so that a position survives a change of scaling and a monitor that has moved.</item>
/// <item>Otherwise the default: just above the notification area on the monitor that holds the tray icon, its right edge level with the icon's
/// (beside the taskbar for a taskbar on the left or right, below it for one on top), 8 DIP from the work-area edge. Without an anchor, the
/// bottom-right corner of the primary monitor's work area.</item>
/// <item>The size is the DIP size at the target monitor's scale, never larger than its work area; the result is always inside a work area.</item>
/// </list>
/// </summary>
public static class ChipPlacement
{
    /// <summary>The gap between the default place and the work-area edge.</summary>
    public const double MarginDips = 8;

    /// <summary>The chip's height, in DIPs.</summary>
    public const double HeightDips = 32;

    /// <summary>The widest the chip gets, in DIPs; its width follows the text up to this.</summary>
    public const double MaxWidthDips = 320;

    /// <summary>The narrowest the chip gets, in DIPs (the icon, a few characters and the countdown).</summary>
    public const double MinWidthDips = 120;

    /// <summary>The chip's rectangle in physical pixels. Never throws; with no monitors it answers a rectangle at the origin.</summary>
    /// <param name="saved">Where the chip was dragged, per monitor, oldest first.</param>
    /// <param name="monitors">The connected monitors.</param>
    /// <param name="taskbarAnchor">The tray icon (or its stand-in) in physical pixels; null when unknown.</param>
    /// <param name="size">The chip's size in DIPs.</param>
    public static PixelRect Compute(IReadOnlyList<ChipPosition> saved, IReadOnlyList<MonitorInfo> monitors, PixelRect? taskbarAnchor, SizeInDips size)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return new PixelRect(0, 0, Math.Max(1, Pixels(size.Width, 1)), Math.Max(1, Pixels(size.Height, 1)));
        }

        for (var i = saved.Count - 1; i >= 0; i--)
        {
            var position = saved[i];
            if (monitors.FirstOrDefault(m => string.Equals(m.Id, position.MonitorId, StringComparison.Ordinal)) is not { } monitor)
            {
                continue;
            }

            var work = monitor.Work;
            var (width, height) = Fit(size, monitor);
            var rect = new PixelRect(
                work.X + Pixels(position.X, monitor.Scale), work.Y + Pixels(position.Y, monitor.Scale), width, height);
            if (work.Contains(rect.CenterX, rect.CenterY))
            {
                return Slide(rect, work, margin: 0);
            }
        }

        return Default(monitors, taskbarAnchor, size);
    }

    /// <summary>
    /// The position to remember for a chip the person dropped at <paramref name="rect"/>: on the monitor that holds its centre (the nearest one when
    /// it is on none), in DIPs from that monitor's work-area origin. Null when there are no monitors.
    /// </summary>
    public static ChipPosition? ToPosition(PixelRect rect, IReadOnlyList<MonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        if (monitors.Count == 0)
        {
            return null;
        }

        var monitor = FlyoutPlacement.MonitorFor(rect, monitors);
        var work = monitor.Work;
        return new ChipPosition(monitor.Id, Math.Round((rect.X - work.X) / monitor.Scale, 2), Math.Round((rect.Y - work.Y) / monitor.Scale, 2));
    }

    private static PixelRect Default(IReadOnlyList<MonitorInfo> monitors, PixelRect? anchor, SizeInDips size)
    {
        var target = anchor is { } a ? FlyoutPlacement.MonitorFor(a, monitors) : monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var work = target.Work;
        var margin = Pixels(MarginDips, target.Scale);
        var (width, height) = Fit(size, target);

        int x;
        int y;
        if (anchor is not { } icon)
        {
            x = work.Right - margin - width;
            y = work.Bottom - margin - height;
        }
        else
        {
            switch (TaskbarEdge(target))
            {
                case Edge.Top:
                    x = icon.Right - width;
                    y = work.Y + margin;
                    break;
                case Edge.Left:
                    x = work.X + margin;
                    y = work.Bottom - margin - height;
                    break;
                case Edge.Right:
                    x = work.Right - margin - width;
                    y = work.Bottom - margin - height;
                    break;
                default:
                    x = icon.Right - width;
                    y = work.Bottom - margin - height;
                    break;
            }
        }

        return Slide(new PixelRect(x, y, width, height), work, margin);
    }

    private enum Edge
    {
        Bottom,
        Top,
        Left,
        Right,
    }

    /// <summary>The side the taskbar is on: the one that lost the most room to it. A taskbar that hides reserves nothing, so the bottom.</summary>
    private static Edge TaskbarEdge(MonitorInfo monitor)
    {
        var (bounds, work) = (monitor.Bounds, monitor.Work);
        var insets = new (Edge Edge, int Amount)[]
        {
            (Edge.Bottom, bounds.Bottom - work.Bottom),
            (Edge.Top, work.Y - bounds.Y),
            (Edge.Left, work.X - bounds.X),
            (Edge.Right, bounds.Right - work.Right),
        };
        var largest = insets.MaxBy(i => i.Amount);
        return largest.Amount > 0 ? largest.Edge : Edge.Bottom;
    }

    /// <summary>The DIP size at the monitor's scale, no larger than its work area, at least one pixel.</summary>
    private static (int Width, int Height) Fit(SizeInDips size, MonitorInfo monitor)
    {
        var work = monitor.Work;
        return (
            Math.Clamp(Pixels(size.Width, monitor.Scale), 1, Math.Max(1, work.Width)),
            Math.Clamp(Pixels(size.Height, monitor.Scale), 1, Math.Max(1, work.Height)));
    }

    /// <summary>
    /// Moves the rectangle (not resizing it) so that it lies inside the work area with <paramref name="margin"/> to spare; where the work area
    /// has less room than that the margin shrinks, down to none, so the result is always inside it.
    /// </summary>
    private static PixelRect Slide(PixelRect rect, PixelRect work, int margin)
    {
        var across = Math.Min(margin, Math.Max(0, (work.Width - rect.Width) / 2));
        var down = Math.Min(margin, Math.Max(0, (work.Height - rect.Height) / 2));
        return rect with
        {
            X = Math.Clamp(rect.X, work.X + across, Math.Max(work.X + across, work.Right - across - rect.Width)),
            Y = Math.Clamp(rect.Y, work.Y + down, Math.Max(work.Y + down, work.Bottom - down - rect.Height)),
        };
    }

    private static int Pixels(double dips, double scale) => (int)Math.Round(dips * scale, MidpointRounding.AwayFromZero);
}
