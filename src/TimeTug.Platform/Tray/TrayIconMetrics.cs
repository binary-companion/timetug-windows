using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TimeTug.Platform.Tray;

/// <summary>The size Windows draws notification-area icons at on this PC.</summary>
public static class TrayIconMetrics
{
    /// <summary>
    /// The small-icon edge in pixels at the system display scale: 16 at 100 percent, 20 at 125, 24 at 150, 32 at 200. The tray
    /// icon files carry exactly these sizes, so Windows picks the matching picture instead of resampling one.
    /// </summary>
    public static int SmallIconSize()
    {
        var size = PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem());
        return size > 0 ? size : 16;
    }
}
