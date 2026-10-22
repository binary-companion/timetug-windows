using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// The About page and the Software Update page of the General pane. About is the Mac's content (the lockup, the tagline, the version, the credit
/// and the licence line) with the Windows version from the package identity, a copyright line and links to the website and the licence.
/// Software Update is not Sparkle: updates come from the Microsoft Store, so the page says that; a development build, which is not in the Store,
/// shows the commit it was built from instead.
/// </summary>
public sealed class AboutViewModel : ObservableObject
{
    public const string WebsiteUrl = "https://binarycompanion.com/";

    public const string LicenceUrl = "https://github.com/binary-companion/timetug-windows/blob/main/LICENSE";

    public const string StoreUpdatesUrl = "ms-windows-store://downloadsandupdates";

    private readonly string _version;
    private readonly string? _commit;
    private readonly bool _isDevelopmentBuild;
    private readonly ISystemLauncher _launcher;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;

    /// <param name="version">The package version ("0.1.0.0").</param>
    /// <param name="commit">The commit the build came from (see <see cref="BuildCommit"/>), or null when unknown.</param>
    /// <param name="isDevelopmentBuild">A development or CI build: not in the Store, so it shows its commit instead of the Store note.</param>
    public AboutViewModel(string version, string? commit, bool isDevelopmentBuild, ISystemLauncher launcher, IUiDispatcher ui, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        _version = version;
        _commit = string.IsNullOrWhiteSpace(commit) ? null : commit;
        _isDevelopmentBuild = isDevelopmentBuild;
        _launcher = launcher;
        _ui = ui;
        _text = text;
    }

    public string Name => _text.GetString("About_Name");

    /// <summary>What a screen reader says for the app icon and name (the Mac's lockup).</summary>
    public string LockupAutomationName => _text.GetString("About_LockupAutomation");

    public string Tagline => _text.GetString("About_Tagline");

    /// <summary>"Version 0.1.0.0": the package version.</summary>
    public string VersionText => _text.Format("About_Version", _version);

    public string CreditText => _text.GetString("About_Credit");

    public string LicenceNote => _text.GetString("About_LicenceNote");

    public string CopyrightText => _text.GetString("About_Copyright");

    public string WebsiteText => _text.GetString("About_Website");

    public string WebsiteAutomationName => _text.GetString("About_WebsiteAutomation");

    public string LicencesText => _text.GetString("About_Licences");

    public string LicencesAutomationName => _text.GetString("About_LicencesAutomation");

    /// <summary>Why the last link did not open, or null.</summary>
    public string? Error
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => Error is not null;

    /// <summary>The Software Update headline (the Mac's "TimeTug 1.2.3").</summary>
    public string UpdateTitle => _text.Format("SoftwareUpdate_Title", _version);

    /// <summary>The line under "Software Update" on the General hub (the Mac shows the version there too).</summary>
    public string HubSubtitle => UpdateTitle;

    /// <summary>Where updates come from: the Store, or for a development build the commit it was built from.</summary>
    public string UpdateStatusText => !_isDevelopmentBuild
        ? _text.GetString("SoftwareUpdate_Store")
        : _commit is null ? _text.GetString("SoftwareUpdate_DevelopmentNoCommit") : _text.Format("SoftwareUpdate_Development", _commit);

    /// <summary>A development package is not in the Store, so there is nothing to open there.</summary>
    public bool ShowStoreLink => !_isDevelopmentBuild;

    public string StoreLinkText => _text.GetString("SoftwareUpdate_StoreLink");

    public string StoreLinkAutomationName => _text.GetString("SoftwareUpdate_StoreLinkAutomation");

    public Task OpenWebsiteAsync() => OpenAsync(WebsiteUrl);

    public Task OpenLicenceAsync() => OpenAsync(LicenceUrl);

    public Task OpenStoreUpdatesAsync() => OpenAsync(StoreUpdatesUrl);

    private async Task OpenAsync(string url)
    {
        var opened = await _launcher.LaunchAsync(url);
        await _ui.InvokeAsync(() => Error = opened ? null : _text.GetString("About_LinkFailed"));
    }
}

/// <summary>Reads the commit a build came from out of its informational version ("1.0.0+8060ec0a1...", which the SDK builds from <c>SourceRevisionId</c>).</summary>
public static class BuildCommit
{
    private const int ShortHash = 9;
    private const int LongestShown = 16;

    /// <summary>
    /// The text after the plus sign: a full commit hash is cut to its first 9 characters (as <c>git rev-parse --short</c> would give), anything else
    /// to 16 characters; null when there is none.
    /// </summary>
    public static string? From(string? informationalVersion)
    {
        var plus = informationalVersion?.IndexOf('+', StringComparison.Ordinal) ?? -1;
        if (plus < 0)
        {
            return null;
        }

        var revision = informationalVersion![(plus + 1)..].Trim();
        if (revision.Length == 0)
        {
            return null;
        }

        var dash = revision.IndexOf('-', StringComparison.Ordinal);
        var head = dash < 0 ? revision : revision[..dash];
        if (head.Length > ShortHash && head.All(char.IsAsciiHexDigit))
        {
            return head[..ShortHash] + (dash < 0 ? string.Empty : revision[dash..]);
        }

        return revision.Length > LongestShown ? revision[..LongestShown] : revision;
    }
}
