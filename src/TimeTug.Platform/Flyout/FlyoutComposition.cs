using TimeTug.Platform.Hotkey;
using TimeTug.Platform.Instance;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Platform.Flyout;

/// <summary>
/// Connects the three ways the flyout opens besides a click on the tray icon (a second launch, the global shortcut) to
/// <see cref="FlyoutHost"/>, in the order startup needs:
/// <list type="number">
/// <item><see cref="ConnectActivation"/> before the engine starts: a second launch that comes in while the app is still starting is
/// held by the single-instance service (one at most), not lost and not acted on.</item>
/// <item><see cref="CompleteStartupAsync"/> once the engine has sent its first state: the held launch is delivered (it opens the
/// flyout) and the stored shortcut is registered. Until then nothing is registered with Windows, so a key press cannot find a
/// half-started app.</item>
/// <item><see cref="Retire"/> when quitting: the triggers are disconnected and the flyout is closed for good.</item>
/// </list>
/// Every trigger ends in <see cref="FlyoutHost"/>, which also holds the rule that the tug wins.
/// </summary>
public sealed class FlyoutComposition : IDisposable
{
    private const string Category = "flyout";

    private readonly IAppInstance? _instance;
    private readonly HotkeyService _hotkey;
    private readonly FlyoutHost _host;
    private readonly ITugPresence _tug;
    private readonly IUiDispatcher _ui;
    private readonly IDiagnostics? _diagnostics;
    private readonly object _gate = new();
    private IDisposable? _activation;
    private IDisposable? _shortcut;
    private bool _retired;

    /// <param name="instance">This copy's single-instance registration; null in a self-test, which has none.</param>
    public FlyoutComposition(IAppInstance? instance, HotkeyService hotkey, FlyoutHost host, ITugPresence tug, IUiDispatcher ui, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(tug);
        ArgumentNullException.ThrowIfNull(ui);
        _instance = instance;
        _hotkey = hotkey;
        _host = host;
        _tug = tug;
        _ui = ui;
        _diagnostics = diagnostics;
    }

    /// <summary>Starts listening for second launches. Call before the engine starts.</summary>
    public void ConnectActivation()
    {
        lock (_gate)
        {
            if (_retired || _activation is not null || _instance is null)
            {
                return;
            }

            _activation = FlyoutTriggers.OpenOnActivation(_instance, _host, _ui);
        }
    }

    /// <summary>
    /// The engine has sent its first state. Delivers the held second launch, then registers the stored shortcut. A shortcut that
    /// cannot be registered is recorded by the hotkey service and never stops startup.
    /// </summary>
    public async Task CompleteStartupAsync()
    {
        lock (_gate)
        {
            if (_retired)
            {
                return;
            }

            _shortcut ??= FlyoutTriggers.ToggleOnHotkey(_hotkey, _host);
        }

        _instance?.MarkReady(_tug);
        try
        {
            await _hotkey.StartAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            // The type only; the app works without a shortcut.
            _diagnostics?.Record("warning", Category, $"the global shortcut could not be started ({e.GetType().Name})");
        }
    }

    /// <summary>Quitting: no trigger opens the flyout any more and an open one is closed. Safe to call more than once.</summary>
    public void Retire()
    {
        IDisposable?[] subscriptions;
        lock (_gate)
        {
            if (_retired)
            {
                return;
            }

            _retired = true;
            subscriptions = [_activation, _shortcut];
            _activation = null;
            _shortcut = null;
        }

        foreach (var subscription in subscriptions)
        {
            subscription?.Dispose();
        }

        // Disposing the host closes an open flyout and makes every later Toggle or Show do nothing.
        _host.Dispose();
    }

    public void Dispose() => Retire();
}
