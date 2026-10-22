using System.Reflection;
using System.Runtime.InteropServices;

namespace TimeTug.Platform;

/// <summary>The app's and Windows' version numbers, for the diagnostics report. Neither is a secret or calendar content.</summary>
public static class AppVersion
{
    /// <summary>The package version, or the assembly's when the process has no package identity.</summary>
    public static string Current()
    {
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or FileNotFoundException)
        {
            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        }
    }

    /// <summary>
    /// The commit the running build was made from: the revision after the plus sign in the app assembly's informational version (the App project
    /// stamps it from <c>git describe</c> at build time). Null when the build has none (no git on the build machine).
    /// </summary>
    public static string? BuildCommit() => Presentation.Settings.BuildCommit.From(
        Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>"10.0.26100.0 (Arm64)".</summary>
    public static string WindowsVersion() => $"{Environment.OSVersion.Version} ({RuntimeInformation.OSArchitecture})";
}
