using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TimeTug.App.Composition;
using TimeTug.App.Settings;
using TimeTug.App.Tray;
using TimeTug.App.Tug;
using TimeTug.App.Views;
using TimeTug.Platform.Flyout;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.Instance;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tray;
using TimeTug.Presentation.Tug;

namespace TimeTug.App;

/// <summary>
/// The running app on the UI thread: it builds the host, keeps a hidden window alive (closing the last visible window ends a WinUI
/// app), puts the icon in the notification area (whose Quit is how a person ends the app), opens the flyout from the icon, a second
/// launch and the shortcut, opens the welcome window once the engine's first state says this is a first run, and quits in the order
/// the engine needs (<see cref="QuitSequence"/>): the flyout is closed, an outstanding takeover is closed, the tray icon is removed,
/// the engine is disposed, then the process exits.
/// <para>
/// Startup order: the host and its services are built; a second launch is listened for (<see cref="FlyoutComposition.ConnectActivation"/>)
/// before the engine starts, and one that arrives early is held; the engine starts; the tray icon appears; the engine's first state
/// arrives; then the held launch is delivered (it opens the flyout) and the stored shortcut is registered; then the welcome window
/// opens when this is a first run.
/// </para>
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "QuitAsync disposes the tray; the shell lives as long as the process and has one ordered shutdown.")]
internal sealed class AppShell
{
    private readonly DispatcherQueue _queue;
    private readonly IDiagnostics _diagnostics;
    private readonly Action<IServiceCollection>? _replace;
    private readonly bool _withTray;
    private readonly IAppInstance? _instance;
    private IHost? _host;
    private Window? _anchor;
    private Window? _failure;
    private WelcomeWindow? _welcome;
    private TrayViewModel? _trayModel;
    private TrayIconService? _tray;
    private FlyoutHost? _flyoutHost;
    private SettingsHost? _settingsHost;
    private FlyoutComposition? _flyout;
    private AppRuntime? _runtime;
    private int _quitting;

    /// <param name="replace">Replaces registrations after the real ones (the self-test scripts the engine host and the OS state this way).</param>
    /// <param name="withTray">False only in a self-test scenario that has nothing to do with the notification area.</param>
    /// <param name="instance">This copy's place among the copies of the app (null in a self-test). The shell tells it when startup has finished; the
    /// composition that opens the flyout subscribes to its <see cref="IAppInstance.Activated"/> before that.</param>
    public AppShell(DispatcherQueue queue, IDiagnostics diagnostics, Action<IServiceCollection>? replace = null, bool withTray = true, IAppInstance? instance = null)
    {
        _instance = instance;
        _queue = queue;
        _diagnostics = diagnostics;
        _replace = replace;
        _withTray = withTray;
    }

    /// <summary>This copy's single-instance registration (null in a self-test). It lives until the process ends, so a launch during shutdown still finds this copy and never starts a second one on its data.</summary>
    public IAppInstance? Instance => _instance;

    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("The app has not started.");

    /// <summary>The first-run welcome window while it is open.</summary>
    public WelcomeWindow? Welcome => _welcome;

    /// <summary>The notification-area icon (null before startup finishes, after quitting, or when startup failed).</summary>
    internal TrayIconService? TrayIcon => _tray;

    /// <summary>What the tray shows and does (null when there is no tray).</summary>
    internal TrayViewModel? TrayModel => _trayModel;

    /// <summary>Opens, closes and places the flyout (null before the services are built).</summary>
    internal FlyoutHost? Flyout => _flyoutHost;

    /// <summary>Opens the Settings window, once (null before the services are built).</summary>
    internal SettingsHost? Settings => _settingsHost;

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
            _flyoutHost = Services.GetRequiredService<FlyoutHost>();
            _settingsHost = Services.GetRequiredService<SettingsHost>();
            // The gear in the flyout's header: the flyout has closed itself, and the Settings window comes forward.
            _flyoutHost.SettingsRequested += OpenSettings;
            _flyout = new FlyoutComposition(
                _instance, Services.GetRequiredService<HotkeyService>(), _flyoutHost, Services.GetRequiredService<ITugPresence>(),
                Services.GetRequiredService<IUiDispatcher>(), _diagnostics);

            // Before the engine starts: a second launch that comes in while this copy is starting is held, not lost.
            _flyout.ConnectActivation();
            await _host.StartAsync();
            _runtime = Services.GetRequiredService<AppRuntime>();
            if (_withTray)
            {
                StartTray();
            }

            var showWelcome = await _runtime.ShouldShowWelcome;

            // The engine is up and has sent its first state: the held second launch opens the flyout and the stored shortcut is registered.
            await _flyout.CompleteStartupAsync();
            if (showWelcome)
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
        var host = new DelegateTrayHost(ShowWelcome, OpenSettings, () => _flyoutHost?.Toggle(), () => runtime.TestTugAsync(), () => new ValueTask(QuitAsync()));
        _trayModel = new TrayViewModel(
            Services.GetRequiredService<IEngineClient>(),
            Services.GetRequiredService<TugController>(),
            Services.GetRequiredService<IUiSettings>(),
            Services.GetRequiredService<ILocalizer>(),
            host,
            Services.GetRequiredService<TimeProvider>(),
            Services.GetRequiredService<IMinuteTicker>());
        _tray = new TrayIconService(_trayModel, Services.GetRequiredService<IUiDispatcher>(), _diagnostics);
        var tray = _tray;
        Services.GetRequiredService<TrayIconIdSource>().Provider = () => tray.IconId;
        _tray.Start();
    }

    /// <summary>Opens the Settings window, or brings the open one forward. Optionally on a pane and with a control highlighted.</summary>
    public void ShowSettings(SettingsPane? pane = null, string? highlightId = null) => _settingsHost?.Show(pane, highlightId);

    private void OpenSettings() => ShowSettings();

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

    /// <summary>Quits in the order <see cref="QuitSequence"/> describes, then exits the app.</summary>
    public async Task QuitAsync(bool exitProcess = true)
    {
        if (Interlocked.Exchange(ref _quitting, 1) == 1)
        {
            return;
        }

        await new QuitSequence(CloseFlyout, CloseOutstandingTakeoverAsync, RemoveTrayIcon, DisposeEngineAsync, _diagnostics).RunAsync();

        if (exitProcess)
        {
            Application.Current.Exit();
        }
    }

    /// <summary>The flyout closes and stays closed (no click, second launch or shortcut opens it again), and the Settings window closes with it.</summary>
    private void CloseFlyout()
    {
        _flyout?.Retire();
        _flyoutHost?.Dispose();
        if (_flyoutHost is { } flyout)
        {
            flyout.SettingsRequested -= OpenSettings;
        }

        _settingsHost?.Dispose();
    }

    /// <summary>The runtime stops listening, applies what was queued and tells the engine an outstanding takeover is closed (nothing to do when startup did not finish).</summary>
    private Task CloseOutstandingTakeoverAsync() => _runtime?.QuitAsync() ?? Task.CompletedTask;

    /// <summary>The icon goes before the engine, so a quit never leaves a ghost icon in the notification area.</summary>
    private void RemoveTrayIcon()
    {
        _tray?.Dispose();
        _trayModel?.Dispose();
        _tray = null;
        _trayModel = null;
    }

    /// <summary>The host stops and disposes the services; the engine is the last of them.</summary>
    private async Task DisposeEngineAsync()
    {
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
