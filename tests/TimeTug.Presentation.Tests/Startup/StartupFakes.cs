using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Tests.Startup;

/// <summary>An observable the test pushes into; a new subscriber gets the latest value when <c>replay</c> is set.</summary>
internal sealed class Feed<T>(bool replay) : IObservable<T>
{
    private readonly List<IObserver<T>> _observers = [];
    private T? _latest;
    private bool _has;

    public int SubscriberCount
    {
        get
        {
            lock (_observers)
            {
                return _observers.Count;
            }
        }
    }

    public void Push(T value)
    {
        IObserver<T>[] targets;
        lock (_observers)
        {
            _latest = value;
            _has = true;
            targets = [.. _observers];
        }

        foreach (var observer in targets)
        {
            observer.OnNext(value);
        }
    }

    public IDisposable Subscribe(IObserver<T> observer)
    {
        lock (_observers)
        {
            _observers.Add(observer);
        }

        if (replay && _has)
        {
            observer.OnNext(_latest!);
        }

        return new Removal(() =>
        {
            lock (_observers)
            {
                _observers.Remove(observer);
            }
        });
    }

    private sealed class Removal(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}

/// <summary>An engine client the test drives: push state and takeovers, read the commands the app sent, in order.</summary>
internal sealed class ScriptedClient : IEngineClient
{
    private readonly object _gate = new();
    private readonly List<string> _commands = [];

    public Feed<AppState> StateFeed { get; } = new(replay: true);

    public Feed<TakeoverRequest> DueFeed { get; } = new(replay: false);

    public Feed<string> WithdrawnFeed { get; } = new(replay: false);

    public Feed<EngineDiagnosticInfo> DiagnosticsFeed { get; } = new(replay: false);

    /// <summary>When set, <see cref="StartAsync"/> throws it (the engine could not start).</summary>
    public Exception? StartFailure { get; set; }

    /// <summary>How many subscribers the takeover and state streams had when the engine was started (the app must be listening first).</summary>
    public (int State, int Due, int Withdrawn)? SubscribersAtStart { get; private set; }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
            {
                return [.. _commands];
            }
        }
    }

    public IEnumerable<string> Terminal => Commands.Where(c => !c.StartsWith("presented:", StringComparison.Ordinal) && c != "start");

    public IObservable<AppState> State => StateFeed;

    public IObservable<TakeoverRequest> TakeoversDue => DueFeed;

    public IObservable<string> TakeoversWithdrawn => WithdrawnFeed;

    public IObservable<EngineDiagnosticInfo> Diagnostics => DiagnosticsFeed;

    public ValueTask StartAsync()
    {
        SubscribersAtStart = (StateFeed.SubscriberCount, DueFeed.SubscriberCount, WithdrawnFeed.SubscriberCount);
        return StartFailure is { } failure ? throw failure : Record("start");
    }

    public ValueTask AddAccountAsync(string kindId) => Record("add:" + kindId);

    public ValueTask RemoveAccountAsync(string connectionId) => Record("remove:" + connectionId);

    public ValueTask ReconnectAccountAsync(string connectionId) => Record("reconnect:" + connectionId);

    public ValueTask CancelAuthorizationAsync() => Record("cancel");

    public ValueTask TakeoverPresentedAsync(string eventId, bool onScreen) => Record($"presented:{eventId}:{(onScreen ? "screen" : "toast")}");

    public ValueTask TakeoverClosedAsync(string eventId) => Record("closed:" + eventId);

    public ValueTask JoinAsync(string eventId) => Record("join:" + eventId);

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration) => Record($"snooze:{eventId}:{(int)duration.TotalSeconds}");

    public ValueTask DismissAsync(string eventId) => Record("dismiss:" + eventId);

    public ValueTask TestTugAsync(string title, string calendarTitle) => Record($"testTug:{title}|{calendarTitle}");

    public ValueTask SetEnabledAsync(bool enabled) => Record("enabled:" + enabled);

    public ValueTask SystemEventAsync(string kind, string? timeZone = null) => Record("system:" + kind);

    private ValueTask Record(string command)
    {
        lock (_gate)
        {
            _commands.Add(command);
        }

        return ValueTask.CompletedTask;
    }
}

internal sealed class MapLocalizer(Dictionary<string, string>? values = null) : ILocalizer
{
    public string GetString(string key) => TryGetString(key) ?? key;

    public string? TryGetString(string key) => values is not null && values.TryGetValue(key, out var value) ? value : null;
}

/// <summary>Everything around one <see cref="AppRuntime"/>: the controller, the fakes it reads and the engine client it talks to.</summary>
internal sealed class RuntimeRig : IAsyncDisposable
{
    public RuntimeRig(Dictionary<string, string>? strings = null)
    {
        Time.SetUtcNow(Requests.Noon);
        Controller = new TugController(Client, Session, Notifications, Monitors, Toasts, Launcher, Diagnostics, Time);
        Controller.PlanChanged += plan =>
        {
            lock (Plans)
            {
                Plans.Add(plan);
            }
        };
        Runtime = new AppRuntime(Client, Controller, Session, Notifications, Monitors, new MapLocalizer(strings), Diagnostics, Time);
    }

    public ScriptedClient Client { get; } = new();

    public FakeSession Session { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    public FakeMonitors Monitors { get; } = new();

    public FakeToasts Toasts { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public RecordingTugDiagnostics Diagnostics { get; } = new();

    public Microsoft.Extensions.Time.Testing.FakeTimeProvider Time { get; } = new();

    public TugController Controller { get; }

    public AppRuntime Runtime { get; }

    public List<TugPlan> Plans { get; } = [];

    public IReadOnlyList<TugPlan> PlansSnapshot
    {
        get
        {
            lock (Plans)
            {
                return [.. Plans];
            }
        }
    }

    public static AppState EmptyState() => new([], [], [], null, null, false, true);

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        Controller.Dispose();
    }
}
