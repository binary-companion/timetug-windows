using TimeTug.Presentation.Abstractions;
using Windows.System;

namespace TimeTug.Platform.OsState;

/// <summary>
/// Opens a meeting link in its default handler. Only web links and the conferencing apps' own schemes are opened;
/// anything else (a file path, another application's scheme) is refused. A failure returns false, never throws.
/// </summary>
public sealed class UrlLauncher : IUrlLauncher
{
    /// <summary>http and https, plus the schemes the Join links of Zoom, Teams, Webex and Slack use.</summary>
    public static IReadOnlySet<string> DefaultSchemes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "zoommtg", "zoomus", "msteams", "webex", "slack" };

    private readonly IReadOnlySet<string> _schemes;
    private readonly Func<Uri, ValueTask<bool>> _launch;

    public UrlLauncher(IReadOnlySet<string>? allowedSchemes = null, Func<Uri, ValueTask<bool>>? launch = null)
    {
        _schemes = allowedSchemes ?? DefaultSchemes;
        _launch = launch ?? (async uri => await Launcher.LaunchUriAsync(uri));
    }

    public async ValueTask<bool> LaunchAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !_schemes.Contains(uri.Scheme))
        {
            return false;
        }

        try
        {
            return await _launch(uri);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
