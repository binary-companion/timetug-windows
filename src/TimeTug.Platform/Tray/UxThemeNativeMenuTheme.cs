using System.Runtime.InteropServices;
using TimeTug.Presentation.Theming;

namespace TimeTug.Platform.Tray;

/// <summary>
/// Colours the menus Windows draws itself (the tray icon's right-click menu) with the process-wide switch Windows' own apps use: uxtheme's
/// <c>SetPreferredAppMode</c> and <c>FlushMenuThemes</c>. Both are exported by number only (135 and 136) and undocumented. A Windows that does
/// not have them keeps its light menus; <see cref="Apply"/> then returns false and never throws.
/// </summary>
public sealed partial class UxThemeNativeMenuTheme : INativeMenuTheme
{
    private const int AppModeDefault = 0;
    private const int AppModeForceDark = 2;
    private const int AppModeForceLight = 3;

    public bool Apply(NativeMenuMode mode)
    {
        var native = mode switch
        {
            NativeMenuMode.Dark => AppModeForceDark,
            NativeMenuMode.Light => AppModeForceLight,
            _ => AppModeDefault,
        };
        try
        {
            _ = SetPreferredAppMode(native);
            FlushMenuThemes();
            return true;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException)
        {
            return false;
        }
    }

    [LibraryImport("uxtheme.dll", EntryPoint = "#135")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SetPreferredAppMode(int mode);

    [LibraryImport("uxtheme.dll", EntryPoint = "#136")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void FlushMenuThemes();
}
