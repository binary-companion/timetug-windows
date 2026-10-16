using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Tug;

/// <summary>
/// The tug state machine. It decides how one takeover request is presented on this PC and owes the engine
/// exactly one terminal command (<c>takeover.closed</c>, <c>join</c>, <c>snooze</c> or <c>dismiss</c>) for it on
/// every path, as the engine's host contract requires.
/// <para>
/// It reacts to engine events and OS-state signals only: it never schedules anything and never reads the OS
/// clock (only the injected <see cref="TimeProvider"/>, to decide "has the meeting ended" and "how late is it").
/// The owner of the controller calls the <c>On...</c> methods; calls are serialised, so toast activations and UI
/// callbacks can arrive from any thread. <see cref="PlanChanged"/> and <see cref="AttentionChanged"/> are raised
/// inside that serialisation: handlers must not block waiting for another controller call.
/// </para>
/// </summary>
public sealed class TugController : ITugActions, IDisposable
{
    private const string Category = "tug";

    private readonly IEngineClient _engine;
    private readonly ISessionState _session;
    private readonly INotificationState _notifications;
    private readonly IMonitorService _monitors;
    private readonly IToastService _toasts;
    private readonly IUrlLauncher _launcher;
    private readonly IDiagnostics _diagnostics;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private AppState? _latestState;
    private TakeoverRequest? _current;
    private int _stateValue = (int)TugState.Hidden;
    private TugPlan _plan = TugPlan.None;
    private bool _needsAttention;

    public TugController(
        IEngineClient engine,
        ISessionState session,
        INotificationState notifications,
        IMonitorService monitors,
        IToastService toasts,
        IUrlLauncher launcher,
        IDiagnostics diagnostics,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(toasts);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(time);
        _engine = engine;
        _session = session;
        _notifications = notifications;
        _monitors = monitors;
        _toasts = toasts;
        _launcher = launcher;
        _diagnostics = diagnostics;
        _time = time;
    }

    /// <summary>What the window layer should draw (a new plan is also raised through <see cref="PlanChanged"/>).</summary>
    public TugPlan Plan => Volatile.Read(ref _plan);

    public TugState State => (TugState)Volatile.Read(ref _stateValue);

    /// <summary>The request the controller still owes a terminal command for, or null.</summary>
    public TakeoverRequest? Current => Volatile.Read(ref _current);

    /// <summary>True while a request is held because a toast was refused or notifications are off: the tray shows its attention state.</summary>
    public bool NeedsAttention => Volatile.Read(ref _needsAttention);

    /// <summary>Raised whenever the plan changes; the window layer redraws from it.</summary>
    public event Action<TugPlan>? PlanChanged;

    /// <summary>Raised when <see cref="NeedsAttention"/> changes.</summary>
    public event Action? AttentionChanged;

    // ---- engine events ------------------------------------------------------------------------------------------

    public async ValueTask OnTakeoverDueAsync(TakeoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        await using var scope = await EnterAsync();

        // A newer request while one is outstanding (the test tug does this): the older one is closed first, so it
        // still gets its one terminal command. The same request again replaces itself and stays outstanding.
        if (_current is { } older && older.EventId != request.EventId)
        {
            Note("info", "takeover replaced by a newer one; the older one is closed");
            await CloseCurrentAsync();
        }

        _current = request;
        await PresentAsync(request);
    }

    /// <summary>
    /// The engine withdraws a takeover only for a meeting that is armed and not yet emitted, so it never concerns
    /// the current request. It is therefore informational here: whether a held request still applies is decided
    /// from the latest engine state when the person returns.
    /// </summary>
    public async ValueTask OnTakeoverWithdrawnAsync(string eventId)
    {
        await using var scope = await EnterAsync();
        if (_current is { } current && current.EventId == eventId)
        {
            Note("info", "takeover withdrawn by the engine for the current request; nothing changes until the person responds or returns");
        }
    }

    public async ValueTask OnEngineStateAsync(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        await using var scope = await EnterAsync();
        _latestState = state;
    }

    // ---- the person responds ------------------------------------------------------------------------------------

    public ValueTask JoinAsync() => JoinCoreAsync(null);

    public ValueTask SnoozeAsync(TimeSpan duration) => SnoozeCoreAsync(null, duration);

    public ValueTask DismissAsync() => DismissCoreAsync(null);

    /// <summary>Join pressed on a toast made for <paramref name="eventId"/>; ignored when that is no longer the current request.</summary>
    public ValueTask JoinAsync(string eventId) => JoinCoreAsync(eventId);

    public ValueTask SnoozeAsync(string eventId, TimeSpan duration) => SnoozeCoreAsync(eventId, duration);

    public ValueTask DismissAsync(string eventId) => DismissCoreAsync(eventId);

    private async ValueTask JoinCoreAsync(string? eventId)
    {
        await using var scope = await EnterAsync();
        if (!CanRespond(eventId) || _current is not { } request)
        {
            return;
        }

        if (request.JoinUrl is not { Length: > 0 } url)
        {
            Note("warning", "join was requested but the meeting has no join link");
            return;
        }

        var opened = false;
        try
        {
            opened = await _launcher.LaunchAsync(url);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Note("error", $"the join link could not be opened ({e.GetType().Name})");
        }

        if (opened)
        {
            await CloseCurrentAsync(client => client.JoinAsync(request.EventId), "join");
            return;
        }

        Note("warning", "the join link could not be opened; showing it to copy instead");
        if (State == TugState.Toast && CanShowFullScreenNow())
        {
            // A toast cannot show a link to copy: promote to the full tug when Windows allows it.
            ShowFullScreen(request, lateBy: null);
            await ReportPresentedAsync(request, onScreen: true);
        }

        Publish(_plan.JoinFailed(url));
    }

    private async ValueTask SnoozeCoreAsync(string? eventId, TimeSpan duration)
    {
        await using var scope = await EnterAsync();
        if (CanRespond(eventId) && _current is { } request)
        {
            await CloseCurrentAsync(client => client.SnoozeAsync(request.EventId, duration), "snooze");
        }
    }

    private async ValueTask DismissCoreAsync(string? eventId)
    {
        await using var scope = await EnterAsync();
        if (CanRespond(eventId) && _current is { } request)
        {
            await CloseCurrentAsync(client => client.DismissAsync(request.EventId), "dismiss");
        }
    }

    /// <summary>A response is possible only while a tug or toast is showing, and only for the request it was made for.</summary>
    private bool CanRespond(string? eventId) =>
        _current is { } request
        && State is TugState.Shown or TugState.ShownWithoutFocus or TugState.Toast
        && (eventId is null || eventId == request.EventId);

    // ---- OS state -----------------------------------------------------------------------------------------------

    /// <summary>The person signed in again: a held request is re-checked.</summary>
    public async ValueTask OnSessionUnlockedAsync()
    {
        await using var scope = await EnterAsync();
        await ReturnAsync("session unlocked");
    }

    /// <summary>An exclusive full-screen app or presentation mode ended: a held request is re-checked.</summary>
    public async ValueTask OnFullScreenEndedAsync()
    {
        await using var scope = await EnterAsync();
        await ReturnAsync("full-screen ended");
    }

    public async ValueTask OnMonitorsChangedAsync()
    {
        await using var scope = await EnterAsync();
        if (_current is { } request && State is TugState.Shown or TugState.ShownWithoutFocus)
        {
            Publish(_plan with { Monitors = BuildMonitors(), Request = request });
        }
    }

    /// <summary>The window layer reports whether the primary tug window actually has the keyboard.</summary>
    public async ValueTask ReportFocusAsync(bool hasKeyboardFocus)
    {
        await using var scope = await EnterAsync();
        if (_current is null)
        {
            return;
        }

        if (State == TugState.Shown && !hasKeyboardFocus)
        {
            SetState(TugState.ShownWithoutFocus);
            Publish(_plan with { ShowClickToRespond = true });
        }
        else if (State == TugState.ShownWithoutFocus && hasKeyboardFocus)
        {
            SetState(TugState.Shown);
            Publish(_plan with { ShowClickToRespond = false });
        }
    }

    /// <summary>The app is quitting: an outstanding request is closed first, so the engine is never left waiting.</summary>
    public async ValueTask OnApplicationQuittingAsync()
    {
        await using var scope = await EnterAsync();
        if (_current is not null)
        {
            Note("info", "the app is quitting; the outstanding takeover is closed");
            await CloseCurrentAsync();
        }
    }

    // ---- presentation -------------------------------------------------------------------------------------------

    private async ValueTask PresentAsync(TakeoverRequest request)
    {
        if (_session.IsLocked)
        {
            Hold(attention: false, "held: the session is locked");
            return;
        }

        if (_notifications.CanShowFullScreen)
        {
            ShowFullScreen(request, lateBy: null);
            await ReportPresentedAsync(request, onScreen: true);
            return;
        }

        // Windows will not let a full-screen window through (exclusive full-screen app, presentation mode): a toast.
        var accepted = false;
        if (!_notifications.ToastsAllowed)
        {
            Note("warning", "notifications are off or quiet time is on: no toast could be shown");
        }
        else
        {
            try
            {
                accepted = await _toasts.ShowAsync(request);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Note("error", $"the toast could not be shown ({e.GetType().Name})");
            }
        }

        if (accepted)
        {
            SetAttention(false);
            SetState(TugState.Toast);
            Publish(TugPlan.ForToast(request));
            await ReportPresentedAsync(request, onScreen: false);
        }
        else
        {
            // Not reported as presented: nothing reached the person. The request stays outstanding until they return.
            Hold(attention: true, "toast refused: the takeover is held until the person returns");
        }
    }

    private async ValueTask ReturnAsync(string signal)
    {
        if (_current is not { } request || State is not (TugState.HeldUntilReturn or TugState.Toast) || _session.IsLocked)
        {
            return;
        }

        var now = _time.GetUtcNow();
        if (now >= request.End)
        {
            Note("info", "missed, meeting over");
            await CloseCurrentAsync();
        }
        else if (IsGone(request))
        {
            Note("info", "meeting no longer on the calendar");
            await CloseCurrentAsync();
        }
        else if (_notifications.CanShowFullScreen)
        {
            Note("info", $"late return ({signal}): showing the tug");
            ShowFullScreen(request, lateBy: now > request.Start ? now - request.Start : null);
            await ReportPresentedAsync(request, onScreen: true);
        }
    }

    /// <summary>
    /// True when the latest engine state is known and no longer lists the meeting at the time it was due. A merged
    /// copy may carry a new id, so the same title and start also count as still being there; a meeting moved to a
    /// different time does not (the engine re-arms it separately and the old request must be closed).
    /// </summary>
    private bool IsGone(TakeoverRequest request) =>
        _latestState is { } state
        && !state.Events.Any(e => e.Start == request.Start && (e.Id == request.EventId || e.Title == request.Title));

    private bool CanShowFullScreenNow() => !_session.IsLocked && _notifications.CanShowFullScreen;

    private void ShowFullScreen(TakeoverRequest request, TimeSpan? lateBy)
    {
        SetAttention(false);
        SetState(TugState.Shown);
        Publish(new TugPlan(TugMode.FullScreen, request, BuildMonitors(), false, null, lateBy));
    }

    private void Hold(bool attention, string reason)
    {
        Note(attention ? "warning" : "info", reason);
        SetState(TugState.HeldUntilReturn);
        SetAttention(attention);
        Publish(TugPlan.None);
    }

    private List<TugMonitorPlan> BuildMonitors()
    {
        var all = _monitors.Monitors;
        if (all.Count == 0)
        {
            return [];
        }

        var primaryId = all.Any(m => m.Id == _monitors.Primary.Id) ? _monitors.Primary.Id : all[0].Id;
        return all.Select(m => new TugMonitorPlan(m, m.Id == primaryId ? TugMonitorRole.Primary : TugMonitorRole.Mirror)).ToList();
    }

    // ---- the engine commands ------------------------------------------------------------------------------------

    private async ValueTask ReportPresentedAsync(TakeoverRequest request, bool onScreen) =>
        await SendAsync(client => client.TakeoverPresentedAsync(request.EventId, onScreen), "presented");

    /// <summary>Ends the current request with <c>takeover.closed</c> (or the given terminal command) and hides the tug.</summary>
    private async ValueTask CloseCurrentAsync(Func<IEngineClient, ValueTask>? terminal = null, string name = "closed")
    {
        var request = _current!;
        _current = null;
        SetAttention(false);
        SetState(TugState.Hidden);
        Publish(TugPlan.None);
        await SendAsync(terminal ?? (client => client.TakeoverClosedAsync(request.EventId)), name);
    }

    private async ValueTask SendAsync(Func<IEngineClient, ValueTask> send, string name)
    {
        try
        {
            await send(_engine);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Note("error", $"the engine did not accept takeover.{name} ({e.GetType().Name})");
        }
    }

    // ---- plumbing -----------------------------------------------------------------------------------------------

    private void SetState(TugState state)
    {
        Volatile.Write(ref _stateValue, (int)state);
        _notifications.SetWatchingForFullScreenEnd(state is TugState.HeldUntilReturn or TugState.Toast);
    }

    private void SetAttention(bool value)
    {
        if (_needsAttention == value)
        {
            return;
        }

        Volatile.Write(ref _needsAttention, value);
        AttentionChanged?.Invoke();
    }

    private void Publish(TugPlan plan)
    {
        Volatile.Write(ref _plan, plan);
        PlanChanged?.Invoke(plan);
    }

    /// <summary>Diagnostics carry the reason only, never the meeting title or link.</summary>
    private void Note(string level, string message) => _diagnostics.Record(level, Category, message);

    private async ValueTask<Scope> EnterAsync()
    {
        await _gate.WaitAsync();
        return new Scope(_gate);
    }

    public void Dispose()
    {
        PlanChanged = null;
        AttentionChanged = null;
        _gate.Dispose();
    }

    private readonly struct Scope(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}
