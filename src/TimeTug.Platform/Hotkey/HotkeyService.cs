using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Hotkey;

/// <summary>
/// The global popup shortcut: what is registered with Windows, what is remembered between runs, and what a press does. There is no
/// default (the Mac has none): nothing is registered until a shortcut is stored, and the recorder that stores one is slice 3.
/// <list type="bullet">
/// <item><see cref="StartAsync"/> registers the stored shortcut. If another program holds it now, the state says so and nothing is
/// registered, never a half-registered key.</item>
/// <item><see cref="SetAsync"/> replaces the shortcut: the old one is released first, then the new one registered; when Windows
/// refuses the new one the old one is taken back, the refusal is stored in the <see cref="HotkeyState"/> (so Settings can say so after a
/// restart) and the result is <see cref="HotkeyStatus.Conflict"/>. At most one shortcut is registered at any time.</item>
/// <item>A press raises <see cref="Pressed"/> on the UI thread, once, and never while the tug is showing.</item>
/// </list>
/// The diagnostics never name another program or its keys; they name only this app's own shortcut.
/// </summary>
public sealed class HotkeyService : IHotkeyControl, IAsyncDisposable, IDisposable
{
    private const string Category = "hotkey";

    private readonly IHotkeyRegistrar _registrar;
    private readonly IHotkeyStore _store;
    private readonly ITugPresence _tug;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly IDiagnostics? _diagnostics;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private HotkeyBinding? _active;
    private HotkeyState _state = HotkeyState.None;
    private bool _started;
    private bool _suspended;
    private volatile bool _disposed;

    public HotkeyService(IHotkeyRegistrar registrar, IHotkeyStore store, ITugPresence tug, IUiDispatcher ui, TimeProvider time, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(registrar);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(tug);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(time);
        _registrar = registrar;
        _store = store;
        _tug = tug;
        _ui = ui;
        _time = time;
        _diagnostics = diagnostics;
        _registrar.Pressed += OnRegistrarPressed;
    }

    /// <summary>What the Settings page shows: the shortcut, whether it works, and whether another program held one that was tried.</summary>
    public HotkeyState State => _state;

    /// <summary>The state changed (after a set, a clear or a start that found a conflict). Raised on the calling thread of that operation.</summary>
    public event Action? StateChanged;

    /// <summary>The shortcut was pressed. Raised on the UI thread; wire it to <c>FlyoutHost.Toggle</c>.</summary>
    public event Action? Pressed;

    /// <summary>Loads the stored shortcut and registers it. Safe to call again (the second call does nothing).</summary>
    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await StartLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Sets the shortcut (null clears it). The result says what happened; the state is stored when it changed.</summary>
    public async Task<HotkeyResult> SetAsync(HotkeyBinding? binding)
    {
        if (_disposed)
        {
            return HotkeyResult.Cleared;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return HotkeyResult.Cleared;
            }

            await StartLockedAsync().ConfigureAwait(false);
            return binding is { } wanted ? await ReplaceAsync(wanted).ConfigureAwait(false) : await ClearAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases the registered shortcut with Windows while the Settings recorder listens for a chord, so that pressing the chord that is
    /// already registered reaches the recorder instead of firing <c>WM_HOTKEY</c>. The shortcut is only paused: the state and the store are
    /// untouched. The caller must follow with <see cref="Resume"/> or <see cref="SetAsync"/> (a set ends the suspension, whether it registers, conflicts or clears; a shortcut that is
    /// refused as invalid is not registered, so the suspension stays and the caller resumes).
    /// Nothing happens when no shortcut is registered, when already suspended, or after disposal.
    /// </summary>
    public async Task SuspendForRecording()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
            {
                return;
            }

            await StartLockedAsync().ConfigureAwait(false);
            if (_suspended || _active is null)
            {
                return;
            }

            _suspended = true;
            await UnregisterAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Registers again the shortcut <see cref="SuspendForRecording"/> released. When another program took the chord meanwhile the shortcut is
    /// stored as a conflict, like one that cannot be registered at start. Nothing happens when not suspended.
    /// </summary>
    public async Task Resume()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || !_suspended)
            {
                return;
            }

            _suspended = false;
            if (_active is not { } paused)
            {
                return;
            }

            if (await TryRegisterAsync(paused).ConfigureAwait(false))
            {
                return;
            }

            Note("warning", $"the shortcut {paused.ToText()} is used by another program, so it is inactive");
            _active = null;
            Commit(new HotkeyState(paused, HotkeyStatus.Conflict, _time.GetUtcNow(), paused));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registrar.Pressed -= OnRegistrarPressed;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _active = null;
            await _registrar.UnregisterAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The window is already gone, which released the shortcut with it.
            Note("info", $"the shortcut was already released at shutdown ({e.GetType().Name})");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        // The registrar works on the window's own thread, so waiting for it from the UI thread cannot deadlock.
        if (!DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)))
        {
            Note("warning", "releasing the shortcut at shutdown took too long");
        }
    }

    private async Task StartLockedAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _state = _store.Load();
        if (_state.Binding is not { } stored)
        {
            return;
        }

        if (await TryRegisterAsync(stored).ConfigureAwait(false))
        {
            _active = stored;
            if (_state.Refused == stored)
            {
                // The program that held it let go: the conflict is over.
                Commit(new HotkeyState(stored, HotkeyStatus.Registered, _time.GetUtcNow()));
            }

            return;
        }

        Note("warning", $"the shortcut {stored.ToText()} is used by another program, so it is inactive");
        Commit(new HotkeyState(stored, HotkeyStatus.Conflict, _time.GetUtcNow(), stored));
    }

    private async Task<HotkeyResult> ClearAsync()
    {
        _suspended = false;
        if (_active is not null)
        {
            _active = null;
            await UnregisterAsync().ConfigureAwait(false);
        }

        Commit(new HotkeyState(null, HotkeyStatus.Cleared, _time.GetUtcNow()));
        return HotkeyResult.Cleared;
    }

    private async Task<HotkeyResult> ReplaceAsync(HotkeyBinding wanted)
    {
        var reason = wanted.Validate();
        if (reason != HotkeyReason.None)
        {
            Note("info", $"the shortcut {wanted.ToText()} was refused: {reason}");
            return HotkeyResult.Invalid(reason);
        }

        // A suspension ends here, whatever the set comes to: the registrar holds nothing now, and this method leaves it holding the new
        // shortcut or, when that is refused, the earlier one.
        var wasSuspended = _suspended;
        _suspended = false;
        if (_active == wanted && !wasSuspended)
        {
            // Registering what this app already holds would fail against itself.
            Commit(new HotkeyState(wanted, HotkeyStatus.Registered, _time.GetUtcNow()));
            return HotkeyResult.Registered;
        }

        var previous = _active;
        if (previous is not null)
        {
            _active = null;
            await UnregisterAsync().ConfigureAwait(false);
        }

        if (await TryRegisterAsync(wanted).ConfigureAwait(false))
        {
            _active = wanted;
            Commit(new HotkeyState(wanted, HotkeyStatus.Registered, _time.GetUtcNow()));
            return HotkeyResult.Registered;
        }

        Note("warning", $"the shortcut {wanted.ToText()} is used by another program");
        if (previous is { } earlier)
        {
            if (await TryRegisterAsync(earlier).ConfigureAwait(false))
            {
                _active = earlier;
                Commit(new HotkeyState(earlier, HotkeyStatus.Conflict, _time.GetUtcNow(), wanted));
            }
            else
            {
                Note("warning", $"the earlier shortcut {earlier.ToText()} could not be taken back; it is inactive");
                Commit(new HotkeyState(earlier, HotkeyStatus.Conflict, _time.GetUtcNow(), earlier));
            }
        }
        else
        {
            Commit(new HotkeyState(null, HotkeyStatus.Conflict, _time.GetUtcNow(), wanted));
        }

        return HotkeyResult.Conflict;
    }

    private async Task<bool> TryRegisterAsync(HotkeyBinding binding)
    {
        try
        {
            return await _registrar.RegisterAsync(binding).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // The window that carries the shortcut is gone or the call failed: the shortcut is not registered, which is a conflict as far as the person can tell.
            Note("error", $"registering the shortcut {binding.ToText()} failed ({e.GetType().Name})");
            return false;
        }
    }

    private async Task UnregisterAsync()
    {
        try
        {
            await _registrar.UnregisterAsync().ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Note("error", $"releasing the shortcut failed ({e.GetType().Name})");
        }
    }

    private void Commit(HotkeyState state)
    {
        _state = state;
        _store.Save(state);
        StateChanged?.Invoke();
    }

    private void OnRegistrarPressed() => _ui.Post(() =>
    {
        if (_disposed || _active is null || _suspended)
        {
            return;
        }

        if (_tug.IsTugShowing)
        {
            Note("info", "the shortcut was ignored because the tug is showing");
            return;
        }

        Pressed?.Invoke();
    });

    private void Note(string level, string message) => _diagnostics?.Record(level, Category, message);
}
