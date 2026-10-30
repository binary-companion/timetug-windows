using System.Runtime.InteropServices;

namespace TimeTug.Platform.Chip;

/// <summary>
/// Keeps a WinUI 3 window from ever becoming the active window or taking the keyboard, even when it is clicked. <c>WS_EX_NOACTIVATE</c> is not enough
/// for WinUI: on a pointer press the framework itself activates the window and moves the focus to its input site, calls that bypass the style (the spike
/// in <c>docs/spikes/2026-10-chip-window-and-jumplist.md</c> read <c>GetForegroundWindow</c> before and after a real click). A <c>WH_CBT</c> hook on the
/// UI thread sees each of those requests first and can refuse it: <c>HCBT_ACTIVATE</c> and <c>HCBT_SETFOCUS</c> for the protected window and its child
/// windows are refused; everything else (other windows of the app, other apps) passes through untouched. Pointer events still reach the window.
/// Use it from the UI thread that owns the window; dispose it before the window closes.
/// </summary>
public static unsafe partial class NoActivateGuard
{
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;
    private const int HcbtSetFocus = 9;
    private const uint GaRoot = 2;

    private static readonly Lock Gate = new();
    private static readonly HashSet<nint> Protected = [];
    private static nint s_hook;
    private static int s_vetoes;

    /// <summary>How many activations and focus requests have been refused since the process started (for tests and diagnostics).</summary>
    public static int Vetoes => Volatile.Read(ref s_vetoes);

    /// <summary>
    /// Starts refusing activation and focus for the top-level window. Call it on the window's UI thread, after the window exists and before it is shown.
    /// The returned handle stops the protection; the hook is removed with the last protected window.
    /// </summary>
    public static IDisposable Protect(nint topLevelWindow)
    {
        if (topLevelWindow == 0)
        {
            throw new ArgumentException("A window handle is required.", nameof(topLevelWindow));
        }

        lock (Gate)
        {
            _ = Protected.Add(topLevelWindow);
            if (s_hook == 0)
            {
                s_hook = SetWindowsHookExW(WhCbt, &Callback, 0, GetCurrentThreadId());
                if (s_hook == 0)
                {
                    _ = Protected.Remove(topLevelWindow);
                    throw new InvalidOperationException("The activation hook could not be installed (Win32 error " + Marshal.GetLastPInvokeError() + ").");
                }
            }
        }

        return new Release(topLevelWindow);
    }

    private static void Unprotect(nint topLevelWindow)
    {
        lock (Gate)
        {
            _ = Protected.Remove(topLevelWindow);
            if (Protected.Count == 0 && s_hook != 0)
            {
                _ = UnhookWindowsHookEx(s_hook);
                s_hook = 0;
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    private static nint Callback(int code, nint wParam, nint lParam)
    {
        if ((code == HcbtActivate || code == HcbtSetFocus) && wParam != 0)
        {
            var root = GetAncestor(wParam, GaRoot);
            bool guarded;
            lock (Gate)
            {
                guarded = root != 0 && Protected.Contains(root);
            }

            if (guarded)
            {
                _ = Interlocked.Increment(ref s_vetoes);
                return 1;
            }
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    private sealed class Release(nint window) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Unprotect(window);
            }
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged[Stdcall]<int, nint, nint, nint> callback, nint module, uint threadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(nint hook);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetAncestor(nint window, uint flags);

    [LibraryImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint GetCurrentThreadId();
}
