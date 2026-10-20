using Pkg = TimeTug.Engine;

namespace TimeTug.App.Composition;

/// <summary>How the engine is created: where it keeps its files, whether it runs on its manual test clock, and the OAuth clients it may offer.</summary>
/// <param name="DataDirectory">The package's LocalState folder in production.</param>
/// <param name="Testing">True only in self-test scenarios.</param>
/// <param name="OAuthClients">Keyed by connector family (<c>google</c>, <c>microsoft</c>); a kind without a client is listed as not configured.</param>
/// <param name="TestClockStart">With <paramref name="Testing"/>: the engine's manual clock starts here (null: now). A scenario with recorded calendar answers uses a fixed instant.</param>
internal sealed record EngineStartup(string DataDirectory, bool Testing, IReadOnlyDictionary<string, Pkg.OAuthClient> OAuthClients, DateTimeOffset? TestClockStart = null)
{
    /// <summary>The package's LocalState folder, or <c>%LOCALAPPDATA%\TimeTug\engine</c> when the process has no package identity.</summary>
    public static string DefaultDataDirectory()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or FileNotFoundException)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeTug", "engine");
        }
    }
}
