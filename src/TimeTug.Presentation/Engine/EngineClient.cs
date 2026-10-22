using TimeTug.Presentation.Abstractions;
using Pkg = global::TimeTug.Engine;

namespace TimeTug.Presentation.Engine;

/// <summary>Wraps the package's engine: streams in, observables out, commands through. It interprets no rules.</summary>
public sealed class EngineClient : IEngineClient, IAsyncDisposable
{
    private readonly Pkg.IEngine _engine;
    private readonly IDiagnostics? _diagnostics;
    private readonly ILocalizer? _text;
    private readonly Subject<AppState> _state;
    private readonly Subject<TakeoverRequest> _due;
    private readonly Subject<string> _withdrawn;
    private readonly Subject<EngineDiagnosticInfo> _engineDiagnostics;
    private readonly CancellationTokenSource _stop = new();
    private Task _pumps = Task.CompletedTask;
    private int _started;
    private int _disposed;

    /// <param name="text">The app's strings, for the section titles the calendars list carries; English when null.</param>
    public EngineClient(Pkg.IEngine engine, IDiagnostics? diagnostics = null, ILocalizer? text = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        _diagnostics = diagnostics;
        _text = text;
        Action<Exception> report = e => _diagnostics?.Record("error", "engine-client", "A subscriber threw " + e.GetType().Name);
        _state = new Subject<AppState>(replayLatest: true, report);
        _due = new Subject<TakeoverRequest>(replayLatest: false, report);
        _withdrawn = new Subject<string>(replayLatest: false, report);
        _engineDiagnostics = new Subject<EngineDiagnosticInfo>(replayLatest: false, report);
    }

    public IObservable<AppState> State => _state;

    public IObservable<TakeoverRequest> TakeoversDue => _due;

    public IObservable<string> TakeoversWithdrawn => _withdrawn;

    public IObservable<EngineDiagnosticInfo> Diagnostics => _engineDiagnostics;

    public async ValueTask StartAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        // Read both streams before the engine starts so that nothing it says first is missed.
        _pumps = Task.WhenAll(
            Task.Run(() => PumpAsync(_engine.State, state => _state.OnNext(EngineMapping.ToAppState(state, _text)), "state")),
            Task.Run(() => PumpAsync(_engine.Events, Handle, "events")));
        await _engine.SendAsync(new Pkg.EngineCommand.Start()).ConfigureAwait(false);
    }

    public ValueTask AddAccountAsync(string kindId) => _engine.SendAsync(new Pkg.EngineCommand.AddAccount(kindId));

    public ValueTask RemoveAccountAsync(string connectionId) => _engine.SendAsync(new Pkg.EngineCommand.RemoveAccount(connectionId));

    public ValueTask ReconnectAccountAsync(string connectionId) => _engine.SendAsync(new Pkg.EngineCommand.ReconnectAccount(connectionId));

    public ValueTask CancelAuthorizationAsync() => _engine.SendAsync(new Pkg.EngineCommand.CancelAuthorization());

    public ValueTask TakeoverPresentedAsync(string eventId, bool onScreen) =>
        _engine.SendAsync(new Pkg.EngineCommand.TakeoverPresented(eventId, onScreen));

    public ValueTask TakeoverClosedAsync(string eventId) => _engine.SendAsync(new Pkg.EngineCommand.TakeoverClosed(eventId));

    public ValueTask JoinAsync(string eventId) => _engine.SendAsync(new Pkg.EngineCommand.Join(eventId));

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration) =>
        _engine.SendAsync(new Pkg.EngineCommand.Snooze(eventId, duration.TotalSeconds));

    public ValueTask DismissAsync(string eventId) => _engine.SendAsync(new Pkg.EngineCommand.Dismiss(eventId));

    public ValueTask TestTugAsync(string title, string calendarTitle) =>
        _engine.SendAsync(new Pkg.EngineCommand.TestTakeover(title, calendarTitle));

    public ValueTask SetEnabledAsync(bool enabled) =>
        _engine.SendAsync(new Pkg.EngineCommand.ApplySettings { Enabled = enabled });

    public ValueTask ApplySettingsAsync(Settings.EngineSettingsPatch patch, string? commandId = null)
    {
        ArgumentNullException.ThrowIfNull(patch);
        return _engine.SendAsync(Settings.EngineSettingsMapping.ToCommand(patch), commandId);
    }

    public ValueTask ForgetCorrectionsAsync() => _engine.SendAsync(new Pkg.EngineCommand.ForgetCorrections());

    public ValueTask MergeEventsAsync(string eventId, string otherEventId, string? commandId = null) =>
        _engine.SendAsync(new Pkg.EngineCommand.Merge([eventId, otherEventId]), commandId);

    public ValueTask UnmergeEventAsync(string eventId, string? commandId = null) =>
        _engine.SendAsync(new Pkg.EngineCommand.Unmerge(eventId), commandId);

    public ValueTask SeparateMembersAsync(string eventId, IReadOnlyList<string> memberIds, string? commandId = null)
    {
        ArgumentNullException.ThrowIfNull(memberIds);
        return _engine.SendAsync(new Pkg.EngineCommand.Separate(eventId, memberIds), commandId);
    }

    public ValueTask SystemEventAsync(string kind, string? timeZone = null) =>
        _engine.SendAsync(new Pkg.EngineCommand.SystemEvent(kind, timeZone));

    public async ValueTask DisposeAsync()
    {
        // A container that registers the client under two types disposes it twice.
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        await _engine.DisposeAsync().ConfigureAwait(false);
        await _pumps.ConfigureAwait(false);
        _stop.Dispose();
    }

    private void Handle(Pkg.EngineEvent engineEvent)
    {
        switch (engineEvent)
        {
            case Pkg.EngineEvent.TakeoverDue due:
                _due.OnNext(EngineMapping.ToRequest(due.Request));
                break;
            case Pkg.EngineEvent.TakeoverWithdrawn withdrawn:
                _withdrawn.OnNext(withdrawn.EventID);
                break;
            case Pkg.EngineEvent.Diagnostic diagnostic:
                var info = new EngineDiagnosticInfo(diagnostic.Info.Level, diagnostic.Info.Category, diagnostic.Info.Message) { CommandId = diagnostic.Info.CommandId };
                _diagnostics?.Record(info.Level, "engine:" + info.Category, info.Message);
                _engineDiagnostics.OnNext(info);
                break;
            default:
                // ready, widget snapshots and join requests are not needed by the app core yet.
                break;
        }
    }

    private async Task PumpAsync<T>(IAsyncEnumerable<T> source, Action<T> handle, string name)
    {
        try
        {
            await foreach (var item in source.WithCancellation(_stop.Token).ConfigureAwait(false))
            {
                try
                {
                    handle(item);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // One bad message must not end the stream.
                    _diagnostics?.Record("error", "engine-client", $"A {name} message could not be handled: {e.GetType().Name}");
                }
            }
        }
        catch (Exception e) when (e is OperationCanceledException or ObjectDisposedException)
        {
            // The client or engine is shutting down.
        }
        catch (Exception e)
        {
            _diagnostics?.Record("error", "engine-client", $"The {name} stream failed: {e.GetType().Name}");
        }
    }
}
