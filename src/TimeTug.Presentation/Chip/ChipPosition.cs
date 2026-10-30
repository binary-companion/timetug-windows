namespace TimeTug.Presentation.Chip;

/// <summary>
/// Where the person dragged the chip on one monitor: the chip's top-left corner in device-independent pixels, measured from that monitor's
/// work-area origin. DIPs relative to the monitor (not physical pixels in the virtual screen) so that a position restored after a change of the
/// monitor's scaling lands in the same place relative to the monitor, and one on a monitor that has since moved still makes sense.
/// </summary>
/// <param name="MonitorId">The monitor's identifier (<c>MonitorInfo.Id</c>).</param>
public readonly record struct ChipPosition(string MonitorId, double X, double Y);
