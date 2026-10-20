using TimeTug.Platform.Tray;

namespace TimeTug.Platform.Tests.Tray;

/// <summary>Where the tray tests find the app's real icon files and the golden images.</summary>
internal static class TrayTestAssets
{
    /// <summary>The app's <c>Assets/Tray</c> files, copied beside the test assembly.</summary>
    public static string IconFolder { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Tray");

    public static TrayIconFiles Icons() => new(IconFolder);

    public static TrayIconRenderer Renderer() => new(Icons());

    /// <summary>The repository root, found from the test binaries (a CI build rewrites source paths, so they cannot be used).</summary>
    public static string RepositoryRoot { get; } = FindRoot();

    /// <summary>The golden folder in the source tree (so an explicit update writes where the files are committed).</summary>
    public static string GoldenFolder() => Path.Combine(RepositoryRoot, "tests", "TimeTug.Platform.Tests", "data");

    /// <summary>The enlarged picture of every digit at every size, kept small in docs/screenshots.</summary>
    public static string ContactSheetPath() => Path.Combine(RepositoryRoot, "docs", "screenshots", "tray-digits-contact-sheet.png");

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TimeTug.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("TimeTug.sln not found above " + AppContext.BaseDirectory);
    }
}
