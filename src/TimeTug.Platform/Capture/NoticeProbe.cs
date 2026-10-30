#if SELFTEST
using System.Runtime.InteropServices;

namespace TimeTug.Platform.Capture;

/// <summary>
/// Self-test only: finds the system message box that another process of the user shows (the notice a second distribution of the app shows before it
/// exits), reads its text and closes it. A message box is a dialog window (class <c>#32770</c>) whose message is a static child control.
/// </summary>
public static unsafe partial class NoticeProbe
{
    private const uint WmClose = 0x0010;

    /// <summary>The message box of <paramref name="processId"/>, or null while there is none.</summary>
    public static (nint Window, string Title, string Text)? Find(int processId)
    {
        var state = new Search(processId);
        var handle = GCHandle.Alloc(state);
        try
        {
            _ = EnumWindows(&Visit, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return state.Found is { } window ? (window, state.Title, state.Text) : null;
    }

    /// <summary>Closes the message box (the same as pressing its close box).</summary>
    public static void Close(nint window) => _ = PostMessageW(window, WmClose, 0, 0);

    private sealed class Search(int processId)
    {
        public int ProcessId { get; } = processId;

        public nint? Found { get; set; }

        public string Title { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public List<string> Texts { get; } = [];
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int Visit(nint window, nint state)
    {
        var search = (Search)GCHandle.FromIntPtr(state).Target!;
        _ = GetWindowThreadProcessId(window, out var process);
        if (process != search.ProcessId || ClassOf(window) != "#32770")
        {
            return 1;
        }

        search.Found = window;
        search.Title = TextOf(window);
        search.Texts.Clear();
        var inner = GCHandle.Alloc(search);
        try
        {
            _ = EnumChildWindows(window, &VisitChild, GCHandle.ToIntPtr(inner));
        }
        finally
        {
            inner.Free();
        }

        search.Text = string.Join(" ", search.Texts);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static int VisitChild(nint child, nint state)
    {
        var search = (Search)GCHandle.FromIntPtr(state).Target!;
        if (ClassOf(child) == "Static" && TextOf(child) is { Length: > 0 } text)
        {
            search.Texts.Add(text);
        }

        return 1;
    }

    private static string ClassOf(nint window)
    {
        var buffer = new char[256];
        var length = Math.Max(0, GetClassNameW(window, buffer, buffer.Length));
        return new string(buffer, 0, length);
    }

    private static string TextOf(nint window)
    {
        var buffer = new char[1024];
        var length = Math.Max(0, GetWindowTextW(window, buffer, buffer.Length));
        return new string(buffer, 0, length);
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumChildWindows(nint parent, delegate* unmanaged[Stdcall]<nint, nint, int> callback, nint state);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetWindowThreadProcessId(nint window, out uint process);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClassNameW(nint window, [Out] char[] text, int capacity);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowTextW(nint window, [Out] char[] text, int capacity);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessageW(nint window, uint message, nint wParam, nint lParam);
}
#endif
