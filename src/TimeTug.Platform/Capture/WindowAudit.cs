#if SELFTEST
using System.Runtime.InteropServices;

namespace TimeTug.Platform.Capture;

/// <summary>One top-level window as Windows sees it.</summary>
/// <param name="Handle">The window handle.</param>
/// <param name="ClassName">The window class.</param>
/// <param name="Title">The window's caption (the app's own windows only ever carry fixed titles, never calendar content).</param>
/// <param name="Visible">The visible style bit (<c>IsWindowVisible</c>).</param>
/// <param name="Cloaked">Hidden by the desktop manager although "visible" (another virtual desktop, or a window being shown).</param>
/// <param name="ExStyle">The extended window style.</param>
/// <param name="Owned">The window has an owner window.</param>
public sealed record TopLevelWindow(nint Handle, string ClassName, string Title, bool Visible, bool Cloaked, long ExStyle, bool Owned)
{
    private const long ToolWindowStyle = 0x00000080;
    private const long AppWindowStyle = 0x00040000;
    private const long NoActivateStyle = 0x08000000;

    public bool ToolWindow => (ExStyle & ToolWindowStyle) != 0;

    public bool AppWindow => (ExStyle & AppWindowStyle) != 0;

    public bool NoActivate => (ExStyle & NoActivateStyle) != 0;

    /// <summary>
    /// True when the Windows shell gives the window a taskbar button and an Alt+Tab entry: it is visible and not cloaked, and either asks for
    /// one (<c>WS_EX_APPWINDOW</c>) or is neither owned nor a tool window (<c>WS_EX_TOOLWINDOW</c>).
    /// </summary>
    public bool HasTaskbarPresence => Visible && !Cloaked && (AppWindow || (!ToolWindow && !Owned));

    public override string ToString() =>
        FormattableString.Invariant($"{ClassName} '{Title}' visible={Visible} cloaked={Cloaked} owned={Owned} toolwindow={ToolWindow} appwindow={AppWindow} noactivate={NoActivate} exstyle=0x{ExStyle:X8} taskbar={HasTaskbarPresence}");
}

/// <summary>
/// Self-test only (it does not exist in a Release build): lists this process's top-level windows with their visibility and extended styles, so a
/// scenario can say exactly which of them the shell would give a taskbar button or an Alt+Tab entry.
/// </summary>
public static unsafe partial class WindowAudit
{
    private const int GwOwner = 4;
    private const int GwlExStyle = -20;
    private const int DwmaCloaked = 14;

    /// <summary>Every top-level window that belongs to this process, visible or not.</summary>
    public static IReadOnlyList<TopLevelWindow> OfThisProcess()
    {
        var found = new List<TopLevelWindow>();
        var handle = GCHandle.Alloc(found);
        try
        {
            _ = EnumWindows(&Collect, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return found;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int Collect(nint window, nint state)
    {
        _ = GetWindowThreadProcessId(window, out var process);
        if (process != Environment.ProcessId)
        {
            return 1;
        }

        var list = (List<TopLevelWindow>)GCHandle.FromIntPtr(state).Target!;
        var className = new char[256];
        var title = new char[256];
        var classLength = Math.Max(0, GetClassNameW(window, className, className.Length));
        var titleLength = Math.Max(0, GetWindowTextW(window, title, title.Length));
        var cloaked = 0;
        _ = DwmGetWindowAttribute(window, DwmaCloaked, ref cloaked, sizeof(int));
        list.Add(new TopLevelWindow(window, new string(className, 0, classLength), new string(title, 0, titleLength), IsWindowVisible(window), cloaked != 0, GetWindowLongPtrW(window, GwlExStyle), GetWindow(window, GwOwner) != 0));
        return 1;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClassNameW(nint window, [Out] char[] text, int capacity);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowTextW(nint window, [Out] char[] text, int capacity);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindowLongPtrW(nint window, int index);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetWindow(nint window, int command);

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(nint window, int attribute, ref int value, int size);
}
#endif
