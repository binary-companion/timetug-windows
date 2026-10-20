using System.Runtime.CompilerServices;
using TimeTug.Platform.Tray;

namespace TimeTug.Platform.Tests.Tray;

/// <summary>Where the tray tests find the app's real icon files and the golden images.</summary>
internal static class TrayTestAssets
{
    /// <summary>The app's <c>Assets/Tray</c> files, copied beside the test assembly.</summary>
    public static string IconFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray");

    public static TrayIconFiles Icons() => new(IconFolder);

    public static TrayIconRenderer Renderer() => new(Icons());

    /// <summary>The golden folder in the source tree (so an explicit update writes where the files are committed).</summary>
    public static string GoldenFolder([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "data"));

    /// <summary>The enlarged picture of every digit at every size, kept small in docs/screenshots.</summary>
    public static string ContactSheetPath([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "..", "docs", "screenshots", "tray-digits-contact-sheet.png"));
}
