using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Theming;

/// <summary>How the menus Windows draws itself (the tray icon's menu is one) are coloured.</summary>
public enum NativeMenuMode
{
    /// <summary>Windows decides: the contrast theme's colours, or the plain light menu a process that never asked for anything else gets.</summary>
    Windows,

    /// <summary>Light, whatever Windows' own apps theme is.</summary>
    Light,

    /// <summary>Dark, whatever Windows' own apps theme is.</summary>
    Dark,
}

/// <summary>Sets how this process's native menus are coloured (the real one is in the platform project; tests use a recorder).</summary>
public interface INativeMenuTheme
{
    /// <summary>Applies the mode to menus opened from now on. False when Windows did not take it (an older Windows, or the call is missing).</summary>
    bool Apply(NativeMenuMode mode);
}

/// <summary>
/// Keeps the native menus the colour the person chose for the app: the Appearance choice (Light or Dark), else Windows' apps theme, with a
/// contrast theme left to Windows. A menu Windows draws is light unless the process says otherwise, which is why the tray menu stayed white on
/// a dark PC. The mode is set when this starts and again whenever the choice or Windows' settings change, so the next menu that opens is right.
/// </summary>
public sealed class NativeMenuThemeFollower : IDisposable
{
    private readonly IPresentationSettingsStore _store;
    private readonly IUiSettings _ui;
    private readonly INativeMenuTheme _native;
    private readonly IDiagnostics? _diagnostics;
    private readonly object _gate = new();
    private NativeMenuMode? _applied;
    private bool _disposed;

    public NativeMenuThemeFollower(IPresentationSettingsStore store, IUiSettings ui, INativeMenuTheme native, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(native);
        _store = store;
        _ui = ui;
        _native = native;
        _diagnostics = diagnostics;
        Refresh();
        store.Changed += Refresh;
        ui.Changed += Refresh;
    }

    /// <summary>The mode most recently accepted by Windows; null while none has been.</summary>
    public NativeMenuMode? Mode
    {
        get
        {
            lock (_gate)
            {
                return _applied;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _store.Changed -= Refresh;
        _ui.Changed -= Refresh;
    }

    private void Refresh()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var mode = AppearanceResolver.NativeMenu(_store.Load().Theme, _ui);
            if (_applied == mode)
            {
                return;
            }

            if (_native.Apply(mode))
            {
                _applied = mode;
                return;
            }

            _diagnostics?.Record("warning", "theme", $"Windows did not take the {mode} mode for its menus; they keep Windows' colours");
        }
    }
}
