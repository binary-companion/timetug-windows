using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Settings;

/// <summary>The Settings window, as the host sees it. The WinUI window implements it; tests use a fake.</summary>
public interface ISettingsWindow
{
    /// <summary>The window closed, whoever closed it.</summary>
    event Action? Closed;

    /// <summary>Shows the window and brings it to the front (restoring it when it is minimized).</summary>
    void Show();

    void Close();
}

/// <summary>Makes the Settings window around a shell.</summary>
public interface ISettingsWindowFactory
{
    ISettingsWindow Create(SettingsShellViewModel shell);
}

/// <summary>
/// Opens the Settings window, once: asking again while it is open focuses it (and moves it to the pane asked for). Every entry point
/// (the tray's Settings row, the flyout's gear, the welcome window, the hotkey recorder) calls <see cref="Show"/>. A tug showing does not close
/// it; the tug is always on top by its own rule. Calls from other threads are posted to the UI thread.
/// </summary>
public sealed class SettingsHost(ISettingsWindowFactory factory, Func<SettingsShellViewModel> createShell, IUiDispatcher ui, IDiagnostics? diagnostics = null) : IDisposable
{
    private const string Category = "settings";

    private ISettingsWindow? _window;
    private SettingsShellViewModel? _shell;
    private bool _disposed;

    /// <summary>True while the Settings window is open.</summary>
    public bool IsOpen => _window is not null;

    /// <summary>The open window's shell (which pane, which highlight); null when closed.</summary>
    public SettingsShellViewModel? Shell => _shell;

    /// <summary>
    /// Opens the window (or focuses the open one). <paramref name="pane"/> and <paramref name="highlightId"/> say where to go; with neither, a
    /// window that is already open stays on the pane the person is looking at and a new one starts on General.
    /// </summary>
    public void Show(SettingsPane? pane = null, string? highlightId = null, GeneralDestination? destination = null)
    {
        if (!ui.HasThreadAccess)
        {
            ui.Post(() => Show(pane, highlightId, destination));
            return;
        }

        if (_disposed)
        {
            return;
        }

        if (_window is { } open && _shell is { } shell)
        {
            if (pane is not null || highlightId is not null || destination is not null)
            {
                shell.Open(pane, highlightId, destination);
            }

            TryShow(open);
            return;
        }

        Create(pane, highlightId, destination);
    }

    /// <summary>Closes the window if it is open.</summary>
    public void Close()
    {
        if (!ui.HasThreadAccess)
        {
            ui.Post(Close);
            return;
        }

        _window?.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (ui.HasThreadAccess)
        {
            _window?.Close();
        }
        else
        {
            ui.Post(() => _window?.Close());
        }
    }

    private void Create(SettingsPane? pane, string? highlightId, GeneralDestination? destination)
    {
        var shell = createShell();
        if (pane is not null || highlightId is not null || destination is not null)
        {
            shell.Open(pane, highlightId, destination);
        }

        ISettingsWindow window;
        try
        {
            window = factory.Create(shell);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            diagnostics?.Record("error", Category, $"the Settings window could not be created ({e.GetType().Name})");
            shell.Dispose();
            return;
        }

        _window = window;
        _shell = shell;
        window.Closed += OnClosed;
        if (!TryShow(window))
        {
            window.Closed -= OnClosed;
            _window = null;
            _shell = null;
            shell.Dispose();
            try
            {
                window.Close();
            }
            catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
            {
                diagnostics?.Record("error", Category, $"the Settings window could not be closed after it failed to show ({e.GetType().Name})");
            }
        }
    }

    private bool TryShow(ISettingsWindow window)
    {
        try
        {
            window.Show();
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            diagnostics?.Record("error", Category, $"the Settings window could not be shown ({e.GetType().Name})");
            return false;
        }
    }

    private void OnClosed()
    {
        if (_window is { } window)
        {
            window.Closed -= OnClosed;
        }

        _window = null;
        _shell?.Dispose();
        _shell = null;
    }
}
