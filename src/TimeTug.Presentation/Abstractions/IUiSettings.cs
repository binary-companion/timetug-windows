namespace TimeTug.Presentation.Abstractions;

/// <summary>The Windows appearance settings the UI follows.</summary>
public interface IUiSettings
{
    bool AnimationsEnabled { get; }

    bool TransparencyEffectsEnabled { get; }

    bool HighContrast { get; }

    /// <summary>True when the taskbar uses the light theme (the tray icon follows it).</summary>
    bool TaskbarUsesLightTheme { get; }

    event Action? Changed;
}
