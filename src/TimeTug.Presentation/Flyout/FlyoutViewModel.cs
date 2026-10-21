using System.Globalization;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Flyout;

/// <summary>
/// What the tray's left-click flyout shows: the header (weekday and "N meetings left"), a card per agenda item, a row per source that
/// needs the person, the empty states and the footer. It mirrors the Mac's <c>DropdownView</c> and <c>PopupRowModel</c>. The engine
/// decides which meetings exist and what phase each is in (past, current, upcoming, which is next, the lead time); this class only
/// turns that into text. The clock matters for wording only: "in 12 min", "Ends 4:00 PM", the progress bar, the date in the header.
/// <para>
/// It listens to the engine's state and to the minute ticker itself and raises <see cref="Changed"/>, only when something it shows
/// differs, from whichever thread caused it; the window layer posts that to the UI thread and must not call back synchronously.
/// </para>
/// </summary>
public sealed class FlyoutViewModel : IDisposable
{
    private readonly IEngineClient _engine;
    private readonly IUrlLauncher _launcher;
    private readonly TimeProvider _time;
    private readonly ILocalizer _text;
    private readonly CultureInfo _culture;
    private readonly FlyoutText _words;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _joinFailed = new(StringComparer.Ordinal);
    private readonly IDisposable _stateSubscription;
    private readonly IDisposable _ticker;
    private AppState? _state;
    private FlyoutView _view;
    private bool _disposed;

    public FlyoutViewModel(IEngineClient engine, IUrlLauncher launcher, TimeProvider time, IMinuteTicker ticker, ILocalizer text, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(ticker);
        ArgumentNullException.ThrowIfNull(text);
        _engine = engine;
        _launcher = launcher;
        _time = time;
        _text = text;
        _culture = culture ?? CultureInfo.CurrentCulture;
        _words = new FlyoutText(text, _culture);
        _view = Build();
        _stateSubscription = engine.State.Subscribe(new StateObserver(OnState));
        _ticker = ticker.Start(Refresh);
    }

    /// <summary>Raised when what the flyout shows differs from before.</summary>
    public event Action? Changed;

    /// <summary>"Tuesday".</summary>
    public string Weekday => View.Weekday;

    /// <summary>"Sep 18 · 3 meetings left".</summary>
    public string Summary => View.Summary;

    /// <summary>One card per agenda item, in the engine's order.</summary>
    public IReadOnlyList<FlyoutRow> Rows => View.Rows;

    /// <summary>Sources that need the person, shown above the header.</summary>
    public IReadOnlyList<ProblemRow> Problems => View.Problems;

    /// <summary>"Tugs you 1 min before".</summary>
    public string Footer => View.Footer;

    /// <summary>The engine has spoken and today has nothing at all: show the empty state instead of the list.</summary>
    public bool IsEmpty => View.IsEmpty;

    /// <summary>There are cards, but no timed meeting is left: show the "No more meetings today" banner above them.</summary>
    public bool NoMoreMeetings => View.NoMoreMeetings;

    public string EmptyTitle => _text.GetString("Flyout_Empty_Title");

    public string EmptySubtitle => _text.GetString("Flyout_Empty_Subtitle");

    public string NoMoreMeetingsText => _text.GetString("Flyout_NoMoreMeetings");

    private FlyoutView View
    {
        get
        {
            lock (_gate)
            {
                return _view;
            }
        }
    }

    /// <summary>
    /// Opens the card's meeting link. Like the Mac's popup this only opens the link; it does not answer a takeover, so nothing is sent to
    /// the engine. A link that cannot be opened is kept on the row (<see cref="FlyoutRow.JoinFailedUrl"/>) so the person can copy it. Never throws.
    /// </summary>
    public async ValueTask JoinAsync(string eventId)
    {
        Uri? url;
        lock (_gate)
        {
            url = _view.Rows.FirstOrDefault(r => string.Equals(r.EventId, eventId, StringComparison.Ordinal))?.JoinUrl;
        }

        if (url is null)
        {
            return;
        }

        var opened = false;
        try
        {
            opened = await _launcher.LaunchAsync(url.AbsoluteUri);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The shell refused or failed: the same outcome as a link that did not open.
        }

        lock (_gate)
        {
            if (opened)
            {
                _joinFailed.Remove(eventId);
            }
            else
            {
                _joinFailed[eventId] = url.AbsoluteUri;
            }
        }

        Refresh();
    }

    /// <summary>
    /// "Sign in again" on an expired account: asks the engine to run its sign-in. Only a connection the flyout is showing as expired is
    /// accepted, so a stale click after the problem went away sends nothing.
    /// </summary>
    public async ValueTask ReconnectAsync(string connectionId)
    {
        bool shown;
        lock (_gate)
        {
            shown = _view.Problems.Any(p => p.HasAction && string.Equals(p.ConnectionId, connectionId, StringComparison.Ordinal));
        }

        if (shown)
        {
            await _engine.ReconnectAccountAsync(connectionId);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _stateSubscription.Dispose();
        _ticker.Dispose();
    }

    private void OnState(AppState state)
    {
        lock (_gate)
        {
            _state = state;
        }

        Refresh();
    }

    private void Refresh()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var view = Build();
            if (view.SameAs(_view))
            {
                return;
            }

            _view = view;
        }

        Changed?.Invoke();
    }

    // ---- building what the flyout shows ---------------------------------------------------------------------------

    private FlyoutView Build()
    {
        var now = _time.GetUtcNow();
        var zone = _time.LocalTimeZone;
        var agenda = _state?.Agenda ?? [];

        // A failed Join is remembered only while its event is still on the agenda.
        foreach (var id in _joinFailed.Keys.Where(id => agenda.All(i => i.EventId != id)).ToList())
        {
            _joinFailed.Remove(id);
        }

        var rows = agenda.Select(item => BuildRow(item, now, zone)).ToArray();

        // The same counts as the Mac's PopupRowModel.meetingsLeft: current and upcoming, timed.
        var left = agenda.Count(i => !i.IsAllDay && i.Phase != AgendaPhase.Past);
        var totalTimed = agenda.Count(i => !i.IsAllDay);
        var hasState = _state is not null;
        return new FlyoutView(
            _words.Weekday(now, zone),
            _words.Summary(now, left, totalTimed, zone),
            rows,
            _state?.Problems.Select(BuildProblem).ToArray() ?? [],
            _words.TugFooter(_state?.Settings.LeadTime ?? TimeSpan.Zero),
            hasState && rows.Length == 0,
            hasState && rows.Length > 0 && left == 0);
    }

    private FlyoutRow BuildRow(AgendaItem item, DateTimeOffset now, TimeZoneInfo zone)
    {
        var kind = item.IsAllDay ? FlyoutRowKind.AllDay
            : item.Phase == AgendaPhase.Past ? FlyoutRowKind.Past
            : item.Phase == AgendaPhase.Current ? FlyoutRowKind.Current
            : item.IsNext ? FlyoutRowKind.Next
            : FlyoutRowKind.Upcoming;

        var separator = _text.GetString("Flyout_MetaSeparator");
        var meta = kind switch
        {
            FlyoutRowKind.AllDay => string.Join(separator, new[] { _text.GetString("Flyout_AllDay"), item.CalendarTitle }.OfType<string>()),
            FlyoutRowKind.Past => Format("Flyout_Done", _words.Duration(item.End - item.ShownStart)),
            FlyoutRowKind.Current => Format("Flyout_Ends", _words.Clock(item.End, zone)),
            _ => string.Join(separator, new[] { _words.Range(item.ShownStart, item.End, zone), item.CalendarTitle }.OfType<string>()),
        };

        var join = kind is FlyoutRowKind.Current or FlyoutRowKind.Next ? item.JoinUrl : null;
        var mergeSummary = MergeSummary(item);
        _joinFailed.TryGetValue(item.EventId, out var failedUrl);
        return new FlyoutRow(
            item.EventId,
            kind,
            kind == FlyoutRowKind.AllDay ? _text.GetString("Flyout_AllDay") : _words.Clock(item.ShownStart, zone),
            item.Title,
            meta,
            item.CalendarTitle,
            item.CalendarColorHex,
            AttendeeText(item.OtherAttendeeCount),
            kind switch
            {
                FlyoutRowKind.Current => _text.GetString("Flyout_Now"),
                FlyoutRowKind.Next => _words.Countdown(item.Start - now),
                _ => null,
            },
            kind == FlyoutRowKind.Current ? FlyoutText.Progress(item.Start, item.End, now) : 0,
            join,
            join is null ? null : _words.JoinLabel(join),
            Format("Flyout_JoinAutomation", item.Title),
            mergeSummary,
            SpokenDescription(item, kind, mergeSummary, now, zone),
            failedUrl,
            failedUrl is null ? null : _text.GetString("Flyout_JoinFailed"));
    }

    private string? AttendeeText(int others) => others switch
    {
        <= 0 => null,
        1 => _text.GetString("Flyout_Attendees_One"),
        _ => Format("Flyout_Attendees_Other", others),
    };

    /// <summary>"Merged with Apple Intelligence · 4 events" / "Merged manually · 2 events" / "3 events merged"; null when not a merge.</summary>
    private string? MergeSummary(AgendaItem item)
    {
        if (!item.IsMerged)
        {
            return null;
        }

        // A rule merge is silent (no label); a model or manual merge says who did it.
        var label = item.MergeKind switch
        {
            "inference" when !string.IsNullOrWhiteSpace(item.MergeEngineName) => Format("Flyout_MergedWith", item.MergeEngineName),
            "userConfirmed" => _text.GetString("Flyout_MergedManually"),
            _ => null,
        };
        return label is null ? Format("Flyout_MergeSummary_Plain", item.MergedEventCount) : Format("Flyout_MergeSummary_Labelled", label, item.MergedEventCount);
    }

    /// <summary>The Mac's accessibility text: title, time, how soon, state, calendar, merge.</summary>
    private string SpokenDescription(AgendaItem item, FlyoutRowKind kind, string? mergeSummary, DateTimeOffset now, TimeZoneInfo zone)
    {
        var parts = new List<string> { item.Title };
        parts.Add(kind == FlyoutRowKind.AllDay ? _text.GetString("Flyout_Spoken_AllDay") : _words.SpokenRange(item.ShownStart, item.End, zone));
        if (kind == FlyoutRowKind.Next)
        {
            parts.Add(Format("Flyout_Spoken_In", _words.SpokenDuration(item.Start - now)));
        }

        if (kind == FlyoutRowKind.Current)
        {
            parts.Add(_text.GetString("Flyout_Spoken_InProgress"));
        }

        if (kind == FlyoutRowKind.Past)
        {
            parts.Add(_text.GetString("Flyout_Spoken_Finished"));
        }

        if (item.CalendarTitle is not null)
        {
            parts.Add(Format("Flyout_Spoken_Calendar", item.CalendarTitle));
        }

        if (mergeSummary is not null)
        {
            parts.Add(mergeSummary);
        }

        return string.Join(_text.GetString("Flyout_Spoken_Separator"), parts);
    }

    private ProblemRow BuildProblem(SourceProblem problem)
    {
        var message = problem.Kind switch
        {
            ProblemKind.NeedsPermission => Format("Flyout_Problem_NeedsPermission", problem.DisplayName),
            ProblemKind.AuthExpired => Format("Flyout_Problem_AuthExpired", problem.DisplayName),
            _ => problem.Detail is { Length: > 0 } detail
                ? Format("Flyout_Problem_Failing", problem.DisplayName, detail)
                : Format("Flyout_Problem_FailingNoReason", problem.DisplayName),
        };

        // Only an expired sign-in can be fixed from here, and only when the engine says which account it is.
        var canSignIn = problem is { Kind: ProblemKind.AuthExpired, ConnectionId: not null };
        return new ProblemRow(
            problem.SourceId,
            problem.Kind,
            message,
            canSignIn ? _text.GetString("Flyout_SignInAgain") : null,
            canSignIn ? Format("Flyout_SignInAgainAutomation", problem.DisplayName) : null,
            problem.ConnectionId);
    }

    private string Format(string key, params object[] arguments) => string.Format(_culture, _text.GetString(key), arguments);

    private sealed record FlyoutView(
        string Weekday,
        string Summary,
        IReadOnlyList<FlyoutRow> Rows,
        IReadOnlyList<ProblemRow> Problems,
        string Footer,
        bool IsEmpty,
        bool NoMoreMeetings)
    {
        public bool SameAs(FlyoutView other) =>
            Weekday == other.Weekday && Summary == other.Summary && Footer == other.Footer && IsEmpty == other.IsEmpty
            && NoMoreMeetings == other.NoMoreMeetings && Rows.SequenceEqual(other.Rows) && Problems.SequenceEqual(other.Problems);
    }

    private sealed class StateObserver(Action<AppState> onNext) : IObserver<AppState>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(AppState value) => onNext(value);
    }
}
