using System.Threading.Channels;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Startup;

/// <summary>
/// Connects the running pieces and owns the order they run in: the engine's streams and the OS-state signals reach the
/// <see cref="TugController"/> through one ordered queue (so an unlock never overtakes the state that came before it), the
/// app is listening before the engine starts, the first engine state decides whether the welcome window opens, and quitting
/// closes an outstanding takeover before anything is torn down.
/// <para>
/// It decides nothing about meetings: it carries engine events to the controller and OS signals to the controller. The
/// observers it registers return at once (they only queue work), because the engine client delivers under a lock.
/// </para>
/// </summary>
public sealed class AppRuntime : IAsyncDisposable
{
    /// <summary>How long the app waits for the engine's first state before it stops waiting (a startup watchdog; the engine decides all timing).</summary>
    public static readonly TimeSpan FirstStateTimeout = TimeSpan.FromSeconds(30);

    private const string Category = "app";

    private readonly IEngineClient _engine;
    private readonly TugController _controller;
    private readonly ISessionState _session;
    private readonly INotificationState _notifications;
    private readonly IMonitorService _monitors;
    private readonly ILocalizer _text;
    private readonly IDiagnostics _diagnostics;
    private readonly TimeProvider _time;
    private readonly Channel<Work> _queue = Channel.CreateUnbounded<Work>(new UnboundedChannelOptions { SingleReader = true });
    private readonly TaskCompletionSource<bool> _welcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<IDisposable> _subscriptions = [];
    private readonly object _gate = new();
    private readonly Action _onUnlocked;
    private readonly Action _onFullScreenEnded;
    private readonly Action _onMonitorsChanged;
    private Task _pump = Task.CompletedTask;
    private Task? _quit;
    private bool _started;
    private bool _stopped;
    private CancellationTokenSource? _firstStateTimeout;
    private CancellationTokenRegistration _firstStateRegistration;

    public AppRuntime(
        IEngineClient engine,
        TugController controller,
        ISessionState session,
        INotificationState notifications,
        IMonitorService monitors,
        ILocalizer text,
        IDiagnostics diagnostics,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(time);
        _engine = engine;
        _controller = controller;
        _session = session;
        _notifications = notifications;
        _monitors = monitors;
        _text = text;
        _diagnostics = diagnostics;
        _time = time;
        _onUnlocked = () => Enqueue("session unlock", _controller.OnSessionUnlockedAsync);
        _onFullScreenEnded = () => Enqueue("full-screen ended", _controller.OnFullScreenEndedAsync);
        _onMonitorsChanged = () => Enqueue("monitors changed", _controller.OnMonitorsChangedAsync);
    }

    /// <summary>
    /// Completes with the engine's first answer to "is this a first run": true when its first state has no accounts. It completes
    /// with false when the engine does not start or produces no state within <see cref="FirstStateTimeout"/>.
    /// </summary>
    public Task<bool> ShouldShowWelcome => _welcome.Task;

    /// <summary>Starts listening, then starts the engine. A second call does nothing. An engine that cannot start throws to the caller.</summary>
    public async ValueTask StartAsync()
    {
        lock (_gate)
        {
            if (_started || _stopped)
            {
                return;
            }

            _started = true;
        }

        WatchFirstState();
        _subscriptions.Add(_engine.State.Subscribe(new Observer<AppState>(OnState)));
        _subscriptions.Add(_engine.TakeoversDue.Subscribe(new Observer<TakeoverRequest>(request => Enqueue("takeover due", () => _controller.OnTakeoverDueAsync(request)))));
        _subscriptions.Add(_engine.TakeoversWithdrawn.Subscribe(new Observer<string>(eventId => Enqueue("takeover withdrawn", () => _controller.OnTakeoverWithdrawnAsync(eventId)))));
        _session.Unlocked += _onUnlocked;
        _notifications.FullScreenEnded += _onFullScreenEnded;
        _monitors.Changed += _onMonitorsChanged;
        _pump = Task.Run(PumpAsync);

        try
        {
            await _engine.StartAsync();
        }
        catch
        {
            StopWatchingFirstState();
            _welcome.TrySetResult(false);
            throw;
        }
    }

    /// <summary>Completes when everything the engine and the OS have said so far has been applied (used by tests and the self-test).</summary>
    public async ValueTask FlushAsync()
    {
        lock (_gate)
        {
            if (!_started || _stopped)
            {
                return;
            }
        }

        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Enqueue("flush", () =>
        {
            applied.TrySetResult();
            return ValueTask.CompletedTask;
        }))
        {
            await applied.Task;
        }
    }

    /// <summary>The Test tug action: the engine's test takeover with the app's own sample title and calendar name.</summary>
    public ValueTask TestTugAsync() => _engine.TestTugAsync(_text.GetString("TestTugTitle"), _text.GetString("TestTugCalendar"));

    /// <summary>
    /// Stops listening, applies what was already queued, then closes an outstanding takeover with <c>takeover.closed</c> so the
    /// engine is never left waiting. Disposing the engine comes after this, from the caller. Safe to call more than once.
    /// </summary>
    public Task QuitAsync()
    {
        lock (_gate)
        {
            return _quit ??= QuitCoreAsync();
        }
    }

    public async ValueTask DisposeAsync() => await QuitAsync();

    private async Task QuitCoreAsync()
    {
        bool wasStarted;
        lock (_gate)
        {
            wasStarted = _started;
            _stopped = true;
        }

        StopWatchingFirstState();
        foreach (var subscription in _subscriptions)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
        _session.Unlocked -= _onUnlocked;
        _notifications.FullScreenEnded -= _onFullScreenEnded;
        _monitors.Changed -= _onMonitorsChanged;
        _welcome.TrySetResult(false);

        if (wasStarted)
        {
            Enqueue("quit", _controller.OnApplicationQuittingAsync);
            _queue.Writer.TryComplete();
            await _pump;
        }
        else
        {
            _queue.Writer.TryComplete();
            await RunAsync(new Work("quit", _controller.OnApplicationQuittingAsync));
        }
    }

    // ---- the first state ----------------------------------------------------------------------------------------

    private void WatchFirstState()
    {
        var timeout = new CancellationTokenSource(FirstStateTimeout, _time);
        _firstStateTimeout = timeout;
        _firstStateRegistration = timeout.Token.Register(() =>
        {
            if (_welcome.TrySetResult(false))
            {
                _diagnostics.Record("warning", Category, $"the engine produced no state within {FirstStateTimeout.TotalSeconds:0} seconds");
            }
        });
    }

    /// <summary>The first state settles the welcome question; every state goes to the controller in order.</summary>
    private void OnState(AppState state)
    {
        if (_welcome.TrySetResult(WelcomeViewModel.ShouldShowAtStartup(state)))
        {
            StopWatchingFirstState();
        }

        Enqueue("engine state", () => _controller.OnEngineStateAsync(state));
    }

    private void StopWatchingFirstState()
    {
        var timeout = Interlocked.Exchange(ref _firstStateTimeout, null);
        if (timeout is null)
        {
            return;
        }

        _firstStateRegistration.Dispose();
        timeout.Dispose();
    }

    // ---- the ordered queue --------------------------------------------------------------------------------------

    private bool Enqueue(string name, Func<ValueTask> run) => _queue.Writer.TryWrite(new Work(name, run));

    private async Task PumpAsync()
    {
        await foreach (var work in _queue.Reader.ReadAllAsync())
        {
            await RunAsync(work);
        }
    }

    private async Task RunAsync(Work work)
    {
        try
        {
            await work.Run();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // One failed step must not stop the ones after it (a takeover still owes its terminal command).
            _diagnostics.Record("error", Category, $"{work.Name} could not be applied ({e.GetType().Name})");
        }
    }

    private sealed record Work(string Name, Func<ValueTask> Run);

    private sealed class Observer<T>(Action<T> onNext) : IObserver<T>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(T value) => onNext(value);
    }
}
