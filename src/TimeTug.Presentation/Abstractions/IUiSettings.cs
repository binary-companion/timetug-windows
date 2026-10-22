namespace TimeTug.Presentation.Abstractions;

/// <summary>The Windows appearance settings the UI follows.</summary>
public interface IUiSettings
{
    bool AnimationsEnabled { get; }

    bool TransparencyEffectsEnabled { get; }

    bool HighContrast { get; }

    /// <summary>True when the taskbar uses the light theme (the tray icon follows it).</summary>
    bool TaskbarUsesLightTheme { get; }

    /// <summary>True when Windows is set to the dark theme for apps (the choice "Use system" in the Appearance pane follows).</summary>
    bool AppsUseDarkTheme { get; }

    event Action? Changed;
}
