namespace TimeTug.Presentation.Settings;

/// <summary>The General pane: launch at login and the popup shortcut recorder. The About page is added with its own step.</summary>
public sealed class GeneralViewModel(LaunchAtLoginViewModel launchAtLogin, HotkeyRecorderViewModel recorder) : IDisposable
{
    /// <summary>The search catalog's ids for the controls of this pane.</summary>
    public static class Ids
    {
        public const string LaunchAtLogin = "launch-at-login";
        public const string PopupShortcut = "popup-shortcut";
    }

    public LaunchAtLoginViewModel LaunchAtLogin { get; } = launchAtLogin;

    public HotkeyRecorderViewModel Recorder { get; } = recorder;

    /// <summary>The window closed: a recording in progress ends and the shortcut is registered again.</summary>
    public void Dispose() => Recorder.Dispose();
}
