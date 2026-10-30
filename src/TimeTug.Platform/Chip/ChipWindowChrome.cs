using System.Runtime.InteropServices;
using TimeTug.Platform.Flyout;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Chip;

/// <summary>
/// The Win32 calls the chip window needs and WinUI does not wrap: a frameless tool window with rounded corners that is never activated, an exact
/// position above the other windows without activation, and the mouse position in physical pixels (a drag cannot be measured in the window's own
/// coordinates, which move with it). The app project may not call native code, so they live here.
/// </summary>
public static partial class ChipWindowChrome
{
    private const int GwlExStyle = -20;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;

    /// <summary>
    /// A frameless popup with rounded corners, no taskbar button and no Alt+Tab entry (<c>WS_EX_TOOLWINDOW</c>), and <c>WS_EX_NOACTIVATE</c>. The style alone does
    /// not keep WinUI from activating the window on a press; <see cref="NoActivateGuard"/> does, and the window installs both.
    /// </summary>
    public static void Style(nint hwnd)
    {
        FlyoutWindowChrome.StyleAsFlyout(hwnd);
        TugWindowChrome.MakeNonActivating(hwnd);
    }

    /// <summary>Puts the window exactly on <paramref name="bounds"/> (virtual-screen pixels) above the other windows and shows it, without activating it. Done twice: moving onto a monitor with another scale makes Windows resize the window on the way.</summary>
    public static void Place(nint hwnd, PixelRect bounds)
    {
        _ = TugWindowChrome.PlaceTopmost(hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height, activate: false);
        _ = TugWindowChrome.PlaceTopmost(hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height, activate: false);
    }

    /// <summary>The mouse position in physical pixels (virtual-screen coordinates).</summary>
    public static (int X, int Y) CursorPosition() => GetCursorPos(out var point) ? (point.X, point.Y) : (0, 0);

    /// <summary>The window's outer rectangle in physical pixels, or null when it cannot be read.</summary>
    public static PixelRect? OuterBounds(nint hwnd) => FlyoutWindowChrome.OuterBounds(hwnd);

    /// <summary>True when the window is a tool window that is not an app window and refuses activation (<c>WS_EX_NOACTIVATE</c>), read back from Windows.</summary>
    public static bool HasChipStyles(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        return (style & WsExToolWindow) != 0 && (style & WsExAppWindow) == 0 && (style & WsExNoActivate) != 0;
    }

    /// <summary>The window that has the keyboard now (0 when none).</summary>
    public static nint ForegroundWindow() => GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);
}
