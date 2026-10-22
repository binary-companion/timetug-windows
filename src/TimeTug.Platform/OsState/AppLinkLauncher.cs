using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.OsState;

/// <summary>
/// Opens the links the app itself chooses: pages of Windows Settings (<c>ms-settings:</c>), the Microsoft Store (<c>ms-windows-store:</c>) and
/// web pages (<c>https:</c>). It is a separate launcher from <see cref="UrlLauncher"/>, which opens meeting links and must never open a
/// <c>ms-settings:</c> link that came in an event. Anything else is refused; a failure returns false, never throws.
/// </summary>
public sealed class AppLinkLauncher : ISystemLauncher
{
    public static IReadOnlySet<string> Schemes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ms-settings", "ms-windows-store", "https" };

    private readonly UrlLauncher _inner;

    public AppLinkLauncher(Func<Uri, ValueTask<bool>>? launch = null) => _inner = new UrlLauncher(Schemes, launch);

    public ValueTask<bool> LaunchAsync(string url) => _inner.LaunchAsync(url);
}
