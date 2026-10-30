using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Presentation.JumpList;

/// <summary>What the jump list's actions do in the rest of the app. The shell implements it over the flyout, Settings and the runtime.</summary>
public interface IJumpActions
{
    /// <summary>Opens the flyout (and leaves it open if it already is).</summary>
    void ShowFlyout();

    /// <summary>Opens the meeting's link through the flyout's own path (a link that cannot be opened shows the flyout with the copy-link state). Never throws.</summary>
    ValueTask JoinAsync(string eventId);

    /// <summary>The engine's test takeover, like the tray menu's Test tug.</summary>
    ValueTask TestTugAsync();

    /// <summary>Opens the Settings window.</summary>
    void OpenSettings();
}

/// <summary>
/// Carries out a jump list action in the running app. Everything here runs on the UI thread; the activation that brought the action arrives on
/// another thread and is posted to it by the trigger. The tug wins: an action that reaches this class while the tug shows is ignored and
/// recorded. "Join next meeting" asks the engine's latest state which meeting that is now: when there is none any more (the list is saved
/// on disk and can outlive the meeting) it opens the flyout instead, so the item is never a dead end.
/// </summary>
public sealed class JumpActionHandler : IDisposable
{
    private const string Category = "jumplist";

    private readonly IJumpActions _actions;
    private readonly ITugPresence _tug;
    private readonly IDiagnostics? _diagnostics;
    private readonly IDisposable _subscription;
    private readonly object _gate = new();
    private AppState? _state;

    public JumpActionHandler(IEngineClient engine, IJumpActions actions, ITugPresence tug, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(tug);
        _actions = actions;
        _tug = tug;
        _diagnostics = diagnostics;
        _subscription = engine.State.Subscribe(new StateObserver(state =>
        {
            lock (_gate)
            {
                _state = state;
            }
        }));
    }

    public void Dispose() => _subscription.Dispose();

    /// <summary>Does what the action asks. Never throws.</summary>
    public async ValueTask HandleAsync(JumpAction action)
    {
        if (_tug.IsTugShowing)
        {
            Note("info", $"the jump list action {JumpArguments.Word(action)} was ignored because the tug is showing");
            return;
        }

        try
        {
            switch (action)
            {
                case JumpAction.Flyout:
                    _actions.ShowFlyout();
                    break;
                case JumpAction.JoinNext:
                    await JoinNextAsync();
                    break;
                case JumpAction.TestTug:
                    await _actions.TestTugAsync();
                    break;
                case JumpAction.Settings:
                    _actions.OpenSettings();
                    break;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            Note("error", $"the jump list action {JumpArguments.Word(action)} failed ({e.GetType().Name})");
        }
    }

    private async ValueTask JoinNextAsync()
    {
        AppState? state;
        lock (_gate)
        {
            state = _state;
        }

        if (JumpListPlan.JoinEventId(state) is not { } eventId)
        {
            Note("info", "there is no meeting to join now; the flyout opens instead");
            _actions.ShowFlyout();
            return;
        }

        await _actions.JoinAsync(eventId);
    }

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
