namespace TimeTug.Presentation.Abstractions;

/// <summary>
/// One display, in virtual-screen pixels. <see cref="WorkArea"/> and <see cref="Dpi"/> are what the flyout needs to sit next to the
/// taskbar at the right scale; both are optional additions, so a monitor built without them is the whole display at 100 percent.
/// </summary>
public sealed record MonitorInfo(string Id, int X, int Y, int Width, int Height, bool IsPrimary)
{
    /// <summary>The part of the display that is not covered by the taskbar; null means all of it.</summary>
    public PixelRect? WorkArea { get; init; }

    /// <summary>The display's effective DPI (96 is 100 percent).</summary>
    public int Dpi { get; init; } = 96;

    /// <summary>The whole display.</summary>
    public PixelRect Bounds => new(X, Y, Width, Height);

    /// <summary>The usable area: <see cref="WorkArea"/>, or the whole display.</summary>
    public PixelRect Work => WorkArea ?? Bounds;

    /// <summary>The scale factor (1.0 at 96 DPI, 1.5 at 144).</summary>
    public double Scale => Dpi / 96.0;
}

/// <summary>The connected displays.</summary>
public interface IMonitorService
{
    IReadOnlyList<MonitorInfo> Monitors { get; }

    MonitorInfo Primary { get; }

    event Action? Changed;
}
