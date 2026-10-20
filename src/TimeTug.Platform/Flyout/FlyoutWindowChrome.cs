using System.Runtime.InteropServices;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Flyout;

/// <summary>
/// The Win32 calls the flyout window needs and WinUI does not wrap: a plain popup without a frame, rounded corners, no taskbar button
/// and no place in Alt+Tab, and an exact position in physical pixels above the taskbar. The app project may not call native code, so
/// they live here.
/// </summary>
public static partial class FlyoutWindowChrome
{
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmcpRound = 2;
    private const int GwlExStyle = -20;
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;

    /// <summary>Makes the window a frameless popup with rounded corners that has no taskbar button and is not listed in Alt+Tab.</summary>
    public static void StyleAsFlyout(nint hwnd)
    {
        TugWindowChrome.RemoveFrame(hwnd);
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        _ = SetWindowLongPtr(hwnd, GwlExStyle, (style | WsExToolWindow) & ~WsExAppWindow);
        var corner = DwmcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmaWindowCornerPreference, ref corner, sizeof(int));
    }

    /// <summary>
    /// Puts the window exactly on <paramref name="bounds"/> (virtual-screen pixels) above the other windows, taskbar included. Done
    /// twice when the window shows: moving a window onto a monitor with another scale makes Windows resize it on the way, and the
    /// second call restores the size that was asked for.
    /// </summary>
    public static void Place(nint hwnd, PixelRect bounds, bool activate)
    {
        _ = TugWindowChrome.PlaceTopmost(hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height, activate: false);
        _ = TugWindowChrome.PlaceTopmost(hwnd, bounds.X, bounds.Y, bounds.Width, bounds.Height, activate);
    }

    /// <summary>The window's outer rectangle in physical pixels.</summary>
    public static PixelRect? OuterBounds(nint hwnd)
    {
        var rect = new NativeRect();
        return GetWindowRect(hwnd, ref rect) ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top) : null;
    }

    /// <summary>True when the window has the extended styles of a tool window (not in Alt+Tab, no taskbar button).</summary>
    public static bool IsToolWindow(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        return (style & WsExToolWindow) != 0 && (style & WsExAppWindow) == 0;
    }

    /// <summary>Which window has the keyboard now: its class name and the id of its process (for diagnosing a flyout that lost the active state).</summary>
    public static string DescribeForeground(nint ownHandle)
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0)
        {
            return "none";
        }

        var name = new char[128];
        var length = GetClassName(foreground, name, name.Length);
        _ = GetWindowThreadProcessId(foreground, out var process);
        return $"{(foreground == ownHandle ? "this window" : new string(name, 0, Math.Max(0, length)))} (process {process}{(process == Environment.ProcessId ? ", this app" : string.Empty)})";
    }

    /// <summary>The window's dots per inch (96 at 100 percent scaling).</summary>
    public static uint DpiOf(nint hwnd) => GetDpiForWindow(hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, ref NativeRect rect);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClassName(nint hwnd, [Out] char[] name, int maxCount);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
