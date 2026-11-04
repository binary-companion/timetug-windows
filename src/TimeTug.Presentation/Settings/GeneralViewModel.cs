namespace TimeTug.Presentation.Settings;

/// <summary>The General pane: launch at login, "Show TimeTug in the taskbar", the popup shortcut recorder, and the About and Software Update pages the hub opens.</summary>
public sealed class GeneralViewModel(
    LaunchAtLoginViewModel launchAtLogin, HotkeyRecorderViewModel recorder, AboutViewModel about, TaskbarSettingsViewModel? taskbar = null) : IDisposable
{
    /// <summary>The search catalog's ids for the controls of this pane.</summary>
    public static class Ids
    {
        public const string LaunchAtLogin = "launch-at-login";
        public const string PopupShortcut = "popup-shortcut";
        public const string TaskbarIcon = "taskbar-icon";
    }

    public LaunchAtLoginViewModel LaunchAtLogin { get; } = launchAtLogin;

    public HotkeyRecorderViewModel Recorder { get; } = recorder;

    public AboutViewModel About { get; } = about;

    /// <summary>Opens the Taskbar page of Windows Settings; null when the app gives none (the pane then has no such row).</summary>
    public TaskbarSettingsViewModel? Taskbar { get; } = taskbar;

    /// <summary>The window closed: a recording in progress ends and the shortcut is registered again.</summary>
    public void Dispose() => Recorder.Dispose();
}
