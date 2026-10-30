using System.Runtime.InteropServices;

namespace TimeTug.Platform.Tug;

/// <summary>
/// The few Win32 calls the tug window needs that WinUI does not wrap: no rounded corners or border on a window as big as
/// the monitor, an exact position above the other topmost windows (the taskbar included), and a check of who has the keyboard.
/// The app project may not call native code, so they live here.
/// </summary>
/// <summary>The system colours a Windows contrast theme defines.</summary>
public enum SystemColorRole
{
    Window,
    WindowText,
    Highlight,
    HighlightText,
    GrayText,
    Hotlight,
}

public static partial class TugWindowChrome
{
    private const int DwmaWindowCornerPreference = 33;
    private const int DwmaBorderColor = 34;
    private const int DwmcpDoNotRound = 1;
    private const uint DwmaColorNone = 0xFFFFFFFE;

    private static readonly nint HwndTopmost = -1;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private const int GwlExStyle = -20;
    private const int GwlStyle = -16;
    private const nint WsPopup = unchecked((int)0x80000000);

    // WS_CAPTION (border and dialog frame), WS_THICKFRAME, WS_SYSMENU, WS_MINIMIZEBOX, WS_MAXIMIZEBOX.
    private const nint FrameStyles = 0x00C00000 | 0x00040000 | 0x00080000 | 0x00020000 | 0x00010000;
    private const nint WsExNoActivate = 0x08000000;
    private const nint WsExToolWindow = 0x00000080;
    private const nint WsExAppWindow = 0x00040000;

    /// <summary>
    /// Square corners and no border colour: a full-monitor window must not leave rounded gaps or a hairline at the edges. It is also a tool window
    /// (<c>WS_EX_TOOLWINDOW</c>, not <c>WS_EX_APPWINDOW</c>), so it has no taskbar button and no Alt+Tab entry by its style alone; the window's
    /// <c>IsShownInSwitchers = false</c> is a request to Explorer that is forgotten when Explorer restarts, the style is not.
    /// </summary>
    public static void StyleAsFullMonitorOverlay(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        _ = SetWindowLongPtr(hwnd, GwlExStyle, (style | WsExToolWindow) & ~WsExAppWindow);
        var corner = DwmcpDoNotRound;
        _ = DwmSetWindowAttribute(hwnd, DwmaWindowCornerPreference, ref corner, sizeof(int));
        var border = DwmaColorNone;
        _ = DwmSetWindowAttribute(hwnd, DwmaBorderColor, ref border, sizeof(uint));
    }

    /// <summary>
    /// Makes the window one that never becomes the active window, even when it is clicked: a mirror must not take the
    /// keyboard from the main window, and when a window closes Windows must not hand the focus to a mirror.
    /// </summary>
    public static void MakeNonActivating(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlExStyle);
        _ = SetWindowLongPtr(hwnd, GwlExStyle, style | WsExNoActivate);
    }

    /// <summary>
    /// Puts the window exactly on the given rectangle (virtual-screen pixels) at the top of the topmost windows. With
    /// <paramref name="activate"/> false it is shown without taking the keyboard, which mirrors must never do.
    /// </summary>
    public static bool PlaceTopmost(nint hwnd, int x, int y, int width, int height, bool activate)
    {
        RemoveFrame(hwnd);
        return SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SwpShowWindow | SwpFrameChanged | (activate ? 0 : SwpNoActivate));
    }

    /// <summary>
    /// Turns the window into a plain popup. WinUI's borderless presenter leaves a dialog frame (a 3 pixel light edge on every
    /// side), which would show the desktop at the monitor's edge; with the frame styles gone the client area is the whole window.
    /// </summary>
    public static void RemoveFrame(nint hwnd)
    {
        var style = GetWindowLongPtr(hwnd, GwlStyle);
        var wanted = (style & ~FrameStyles) | WsPopup;
        if (wanted != style)
        {
            _ = SetWindowLongPtr(hwnd, GwlStyle, wanted);
        }
    }

    /// <summary>One of the Windows system colours that a contrast theme sets, as red, green and blue.</summary>
    public static (byte R, byte G, byte B) SystemColor(SystemColorRole role)
    {
        var index = role switch
        {
            SystemColorRole.Window => 5,
            SystemColorRole.WindowText => 8,
            SystemColorRole.Highlight => 13,
            SystemColorRole.HighlightText => 14,
            SystemColorRole.GrayText => 17,
            _ => 26,
        };
        var colorRef = GetSysColor(index);
        return ((byte)(colorRef & 0xFF), (byte)((colorRef >> 8) & 0xFF), (byte)((colorRef >> 16) & 0xFF));
    }

    /// <summary>The window's outer rectangle, the visible frame rectangle DWM draws and the client rectangle, with its styles (for diagnosing a window that does not cover its monitor).</summary>
    public static string Describe(nint hwnd)
    {
        var outer = new Rect();
        var frame = new Rect();
        var client = new Rect();
        _ = GetWindowRect(hwnd, ref outer);
        _ = GetClientRect(hwnd, ref client);
        _ = DwmGetWindowAttribute(hwnd, 9, ref frame, 16);
        var style = GetWindowLongPtr(hwnd, -16);
        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle);
        return $"window {outer}, visible frame {frame}, client {client}, style 0x{style:X}, exstyle 0x{exStyle:X}";
    }

    /// <summary>The size of the window's drawable area; equal to the window's size when it has no frame.</summary>
    public static (int Width, int Height) ClientSize(nint hwnd)
    {
        var client = new Rect();
        _ = GetClientRect(hwnd, ref client);
        return (client.Right - client.Left, client.Bottom - client.Top);
    }

    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom})";
    }

    /// <summary>True when the window is the one Windows currently sends keyboard input to.</summary>
    public static bool IsForeground(nint hwnd) => hwnd != 0 && GetForegroundWindow() == hwnd;

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll", EntryPoint = "DwmSetWindowAttribute")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmSetWindowAttribute(nint hwnd, int attribute, ref uint value, int size);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, ref Rect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hwnd, ref Rect rect);

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint hwnd, int attribute, ref Rect value, int size);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetSysColor(int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
