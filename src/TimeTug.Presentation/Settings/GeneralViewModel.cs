namespace TimeTug.Presentation.Settings;

/// <summary>The General pane: launch at login, the popup shortcut recorder, and the About and Software Update pages the hub opens.</summary>
public sealed class GeneralViewModel(LaunchAtLoginViewModel launchAtLogin, HotkeyRecorderViewModel recorder, AboutViewModel about) : IDisposable
{
    /// <summary>The search catalog's ids for the controls of this pane.</summary>
    public static class Ids
    {
        public const string LaunchAtLogin = "launch-at-login";
        public const string PopupShortcut = "popup-shortcut";
    }

    public LaunchAtLoginViewModel LaunchAtLogin { get; } = launchAtLogin;

    public HotkeyRecorderViewModel Recorder { get; } = recorder;

    public AboutViewModel About { get; } = about;

    /// <summary>The window closed: a recording in progress ends and the shortcut is registered again.</summary>
    public void Dispose() => Recorder.Dispose();
}
