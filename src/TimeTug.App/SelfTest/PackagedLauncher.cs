#if SELFTEST
using System.Runtime.InteropServices;

namespace TimeTug.App.SelfTest;

/// <summary>
/// Starts the packaged app the way the shell does when a person chooses a jump list item: <c>IApplicationActivationManager.ActivateApplication</c>
/// with the app user model id and the item's arguments (the spike, docs/spikes/2026-10-chip-window-and-jumplist.md, proved the arguments arrive on
/// the command line). With <paramref name="appUserModelId"/> it starts another package's app instead (the cross-flavour scenario). Self-test only.
/// </summary>
internal static class PackagedLauncher
{
    /// <summary>Starts the app with <paramref name="arguments"/>; returns the new process id, or null with the reason when Windows refused.</summary>
    public static (uint? ProcessId, string? Error) Activate(string? arguments, string? appUserModelId = null)
    {
        try
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            var id = appUserModelId ?? Windows.ApplicationModel.AppInfo.Current.AppUserModelId;
            var hr = manager.ActivateApplication(id, arguments, 0, out var processId);
            return hr == 0 ? (processId, null) : (null, "ActivateApplication returned 0x" + hr.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is COMException or InvalidCastException or InvalidOperationException)
        {
            return (null, e.GetType().Name + " 0x" + e.HResult.ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, int options, out uint processId);

        [PreserveSig]
        int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray, [MarshalAs(UnmanagedType.LPWStr)] string verb, out uint processId);

        [PreserveSig]
        int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, nint itemArray, out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private class ApplicationActivationManager
    {
    }
}
#endif
