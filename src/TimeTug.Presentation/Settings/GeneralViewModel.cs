namespace TimeTug.Presentation.Settings;

/// <summary>The General pane: launch at login. The shortcut recorder and the About page are added with their own steps.</summary>
public sealed class GeneralViewModel(LaunchAtLoginViewModel launchAtLogin) : IDisposable
{
    /// <summary>The search catalog's ids for the controls of this pane.</summary>
    public static class Ids
    {
        public const string LaunchAtLogin = "launch-at-login";
    }

    public LaunchAtLoginViewModel LaunchAtLogin { get; } = launchAtLogin;

    public void Dispose()
    {
    }
}
