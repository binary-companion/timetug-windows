using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.JumpList;

/// <summary>
/// Keeps the jump list in step with the engine's state. It saves the base list (without "Join next meeting") at <see cref="Start"/>,
/// before the first state, because Windows keeps the list on disk and a stale "Join next meeting" can outlive the meeting that justified
/// it; then saves again only when the <see cref="JumpListPlan"/> changes, never on every state; and saves the base list again in
/// <see cref="StopAsync"/> when the app quits. A failure to update is recorded (its type only) and ignored; the next change retries.
/// Item names are the app's own generic strings, so no title, link or calendar name ever reaches the list or the log. When the system
/// cannot have a jump list the service says so once and does nothing.
/// </summary>
public sealed class JumpListService
{
    /// <summary>How long quitting waits for the last save.</summary>
    public static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(3);

    private const string Category = "jumplist";

    private readonly IEngineClient _engine;
    private readonly IJumpListPlatform _platform;
    private readonly ILocalizer _text;
    private readonly TimeProvider _time;
    private readonly IDiagnostics? _diagnostics;
    private readonly object _gate = new();
    private IDisposable? _subscription;
    private JumpListPlan? _wanted;
    private JumpListPlan? _saved;
    private Task _pump = Task.CompletedTask;
    private bool _pumping;
    private bool _started;
    private bool _supported;
    private bool _stopped;

    public JumpListService(IEngineClient engine, IJumpListPlatform platform, ILocalizer text, TimeProvider time, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(time);
        _engine = engine;
        _platform = platform;
        _text = text;
        _time = time;
        _diagnostics = diagnostics;
    }

    /// <summary>The plan last saved successfully (null before the first save, or when saving never worked).</summary>
    public JumpListPlan? Saved
    {
        get
        {
            lock (_gate)
            {
                return _saved;
            }
        }
    }

    /// <summary>Saves the base list, then follows the engine's state. A second call does nothing.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started || _stopped)
            {
                return;
            }

            _started = true;
        }

        if (!Supported())
        {
            return;
        }

        lock (_gate)
        {
            _supported = true;
        }

        Request(JumpListPlan.Base);
        var subscription = _engine.State.Subscribe(new StateObserver(state => Request(JumpListPlan.For(state))));
        lock (_gate)
        {
            if (_stopped)
            {
                subscription.Dispose();
                return;
            }

            _subscription = subscription;
        }
    }

    /// <summary>Stops following the state and saves the base list, so nothing stale is left for the next run. Waits a few seconds at most.</summary>
    public async Task StopAsync()
    {
        IDisposable? subscription;
        Task pump;
        bool supported;
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _stopped = true;
            supported = _supported;
            subscription = _subscription;
            _subscription = null;
        }

        subscription?.Dispose();
        if (supported)
        {
            lock (_gate)
            {
                _wanted = JumpListPlan.Base;
                StartPumpLocked();
                pump = _pump;
            }

            try
            {
                await pump.WaitAsync(StopTimeout, _time).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Note("warning", "saving the jump list on quit took too long; it is left as it is");
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // The pump records its own failures; quitting never fails because of the list.
                Note("warning", $"saving the jump list on quit failed ({e.GetType().Name})");
            }
        }
    }

    /// <summary>Completes when every save requested so far has finished (used by tests and the self-test).</summary>
    public Task FlushAsync()
    {
        lock (_gate)
        {
            return _pump;
        }
    }

    private bool Supported()
    {
        bool supported;
        try
        {
            supported = _platform.IsSupported;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Note("info", $"the jump list is not available ({e.GetType().Name})");
            return false;
        }

        if (!supported)
        {
            Note("info", "the jump list is not supported here; the app has none");
        }

        return supported;
    }

    private void Request(JumpListPlan plan)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _wanted = plan;
            StartPumpLocked();
        }
    }

    private void StartPumpLocked()
    {
        if (_pumping || _wanted is null || _wanted == _saved)
        {
            return;
        }

        _pumping = true;
        _pump = PumpAsync();
    }

    private async Task PumpAsync()
    {
        while (true)
        {
            JumpListPlan plan;
            lock (_gate)
            {
                if (_wanted is not { } wanted || wanted == _saved)
                {
                    _pumping = false;
                    return;
                }

                plan = wanted;
            }

            var saved = false;
            try
            {
                await _platform.SaveAsync(plan.Actions.Select(Entry).ToList()).ConfigureAwait(false);
                saved = true;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // A save that ends cancelled counts as a failure too: the loop must always leave _pumping false.
                Note("warning", $"updating the jump list failed ({e.GetType().Name})");
            }

            lock (_gate)
            {
                if (!saved)
                {
                    // Leave the wanted plan as it is: the next change (or the next state) tries again, and this loop must not spin.
                    _pumping = false;
                    return;
                }

                _saved = plan;
            }

            Note("info", $"the jump list was saved with {plan.Actions.Count} tasks");
        }
    }

    private JumpListEntry Entry(JumpAction action) => new(_text.GetString(NameKey(action)), JumpArguments.For(action));

    private static string NameKey(JumpAction action) => action switch
    {
        JumpAction.Flyout => "JumpList_Flyout",
        JumpAction.JoinNext => "JumpList_JoinNext",
        JumpAction.TestTug => "JumpList_TestTug",
        JumpAction.Settings => "JumpList_Settings",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    private void Note(string level, string message) => _diagnostics?.Record(level, Category, message);

    private sealed class StateObserver(Action<AppState> onState) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => onState(value);
    }
}
