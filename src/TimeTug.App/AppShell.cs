using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TimeTug.App.Composition;
using TimeTug.App.Tray;
using TimeTug.App.Tug;
using TimeTug.App.Views;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tray;
using TimeTug.Presentation.Tug;

namespace TimeTug.App;

/// <summary>
/// The running app on the UI thread: it builds the host, keeps a hidden window alive (closing the last visible window ends a WinUI
/// app), puts the icon in the notification area (whose Quit is how a person ends the app), opens the welcome window once the
/// engine's first state says this is a first run, and quits in the order the engine needs: the tray icon is removed, an outstanding
/// takeover is closed, the engine is disposed, then the process exits.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "QuitAsync disposes the tray; the shell lives as long as the process and has one ordered shutdown.")]
internal sealed class AppShell
{
    private readonly DispatcherQueue _queue;
    private readonly IDiagnostics _diagnostics;
    private readonly Action<IServiceCollection>? _replace;
    private readonly bool _withTray;
    private IHost? _host;
    private Window? _anchor;
    private Window? _failure;
    private WelcomeWindow? _welcome;
    private TrayViewModel? _trayModel;
    private TrayIconService? _tray;
    private int _quitting;

    /// <param name="replace">Replaces registrations after the real ones (the self-test scripts the engine host and the OS state this way).</param>
    /// <param name="withTray">False only in a self-test scenario that has nothing to do with the notification area.</param>
    public AppShell(DispatcherQueue queue, IDiagnostics diagnostics, Action<IServiceCollection>? replace = null, bool withTray = true)
    {
        _queue = queue;
        _diagnostics = diagnostics;
        _replace = replace;
        _withTray = withTray;
    }

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("The app has not started.");

    /// <summary>The first-run welcome window while it is open.</summary>
    public WelcomeWindow? Welcome => _welcome;

    /// <summary>The notification-area icon (null before startup finishes, after quitting, or when startup failed).</summary>
    internal TrayIconService? TrayIcon => _tray;

    /// <summary>What the tray shows and does (null when there is no tray).</summary>
    internal TrayViewModel? TrayModel => _trayModel;

    /// <summary>True when startup failed and the failure window is showing.</summary>
    public bool Failed => _failure is not null;

    /// <summary>The window that tells the person TimeTug could not start (null when it started).</summary>
    public Window? FailureWindow => _failure;

    /// <summary>Why startup failed (the exception's type and message; never calendar content), or null.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Starts everything. A failure is recorded and shown in a window; this never throws.</summary>
    public async Task RunAsync()
    {
        try
        {
            _anchor = new Window { Title = "TimeTug" };
            _host = AppComposition.CreateHost(_queue, _diagnostics, _replace);
            await _host.StartAsync();
            if (_withTray)
            {
                StartTray();
            }

            if (await Services.GetRequiredService<AppRuntime>().ShouldShowWelcome)
            {
                ShowWelcome();
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            FailureReason = $"{e.GetType().Name}: {e.Message}";
            _diagnostics.Record("error", "startup", "TimeTug could not start. " + FailureReason);
            ShowFailure();
        }
    }

    /// <summary>Puts the icon in the notification area. The person quits from its menu, so a failure to create it is recorded, not fatal.</summary>
    private void StartTray()
    {
        var runtime = Services.GetRequiredService<AppRuntime>();
        var host = new DelegateTrayHost(ShowWelcome, () => runtime.TestTugAsync(), () => new ValueTask(QuitAsync()));
        _trayModel = new TrayViewModel(
            Services.GetRequiredService<IEngineClient>(),
            Services.GetRequiredService<TugController>(),
            Services.GetRequiredService<IUiSettings>(),
            Services.GetRequiredService<ILocalizer>(),
            host,
            Services.GetRequiredService<TimeProvider>(),
            Services.GetRequiredService<IMinuteTicker>());
        _tray = new TrayIconService(_trayModel, Services.GetRequiredService<IUiDispatcher>(), _diagnostics);
        _tray.Start();
    }

    public void ShowWelcome()
    {
        if (_welcome is not null)
        {
            _welcome.Activate();
            return;
        }

        var window = WelcomeWindowFactory.Create(
            Services.GetRequiredService<IEngineClient>(),
            Services.GetRequiredService<AccountFormService>(),
            Services.GetRequiredService<ILocalizer>(),
            Services.GetRequiredService<TimeProvider>());
        window.Closed += (_, _) => _welcome = null;
        _welcome = window;
        window.Activate();
    }

    /// <summary>Quits in order: the runtime closes an outstanding takeover, the host stops and disposes the services (the engine last), then the app exits.</summary>
    public async Task QuitAsync(bool exitProcess = true)
    {
        if (Interlocked.Exchange(ref _quitting, 1) == 1)
        {
            return;
        }

        // The icon goes first, so a quit never leaves a ghost icon in the notification area.
        _tray?.Dispose();
        _trayModel?.Dispose();
        _tray = null;
        _trayModel = null;

        try
        {
            if (_host is not null)
            {
                await _host.StopAsync();
                if (_host is IAsyncDisposable disposable)
                {
                    await disposable.DisposeAsync();
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _diagnostics.Record("error", "shutdown", $"shutting down raised {e.GetType().Name}: {e.Message} {string.Join(" <- ", (e.StackTrace ?? string.Empty).Split('\n').Take(6).Select(l => l.Trim()))}");
        }

        if (exitProcess)
        {
            Application.Current.Exit();
        }
    }

    private void ShowFailure()
    {
        try
        {
            var window = new StartupFailureWindow(new ResourceLoaderLocalizer(), () => _ = QuitAsync());
            _failure = window;
            window.Activate();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Even the failure window failed: leave nothing running that nobody can see.
            _diagnostics.Record("error", "startup", $"the failure window could not be shown ({e.GetType().Name})");
            Application.Current.Exit();
        }
    }
}
