using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Settings;

/// <summary>Adapts <see cref="SettingsWindow"/> to what <see cref="SettingsHost"/> needs.</summary>
internal sealed class SettingsWindowSurface : ISettingsWindow
{
    public SettingsWindowSurface(SettingsWindow window)
    {
        Window = window;
        window.WindowClosed += () => Closed?.Invoke();
    }

    public SettingsWindow Window { get; }

    public event Action? Closed;

    public void Show() => Window.ShowAndFocus();

    public void Close() => Window.Close();
}

/// <summary>Makes a Settings window around a shell, each time the window opens.</summary>
internal sealed class SettingsWindowFactory(
    SettingsText text, ILocalizer localizer, IPresentationSettingsStore store, IMonitorService monitors, IUiSettings ui,
    SettingsPaneModels models) : ISettingsWindowFactory
{
    /// <summary>How many windows have been made (for the self-test).</summary>
    public int Created { get; private set; }

    /// <summary>The window made last (for the self-test).</summary>
    public SettingsWindowSurface? Last { get; private set; }

    public ISettingsWindow Create(SettingsShellViewModel shell)
    {
        var surface = new SettingsWindowSurface(new SettingsWindow(shell, text, localizer, store, monitors, ui, models));
        Created++;
        Last = surface;
        return surface;
    }
}
