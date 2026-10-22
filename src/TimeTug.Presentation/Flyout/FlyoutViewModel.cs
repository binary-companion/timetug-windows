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
    private readonly IDisposable _diagnosticSubscription;
    private readonly List<string> _pending = [];
    private readonly IDisposable _ticker;
    private AppState? _state;
    private FlyoutView _view;
    private bool _disposed;
    private string? _notice;
    private int _noticeVersion;
    private int _commandCounter;

    /// <summary>How long the message about a merge that did not go through stays.</summary>
    public static readonly TimeSpan NoticeDuration = TimeSpan.FromSeconds(8);

    private const int MaxPendingCommands = 32;

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
        _diagnosticSubscription = engine.Diagnostics.Subscribe(new DiagnosticObserver(OnDiagnostic));
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
            shown = _view.Problems.Any(p => p.HasAction && !p.OpensAccounts && string.Equals(p.ConnectionId, connectionId, StringComparison.Ordinal));
        }

        if (shown)
        {
            await _engine.ReconnectAccountAsync(connectionId);
        }
    }

    /// <summary>A short sentence about the last merge, unmerge or split that did not go through; null when there is none.</summary>
    public string? Notice => View.Notice;

    /// <summary>
    /// "Merge with…": merges the card with another event the engine listed for it (<c>dedup.merge</c>, two ids). The engine republishes its
    /// state, so there is nothing to refresh; a card that has gone stale shows <see cref="Notice"/> instead. Never throws.
    /// </summary>
    public ValueTask MergeAsync(string eventId, string otherEventId) => ActAsync(
        view => view.Rows.FirstOrDefault(r => IsEvent(r, eventId)) is { } row && row.Candidates.Any(c => string.Equals(c.EventId, otherEventId, StringComparison.Ordinal)),
        id => _engine.MergeEventsAsync(eventId, otherEventId, id));

    /// <summary>"Unmerge": splits the merged card into its events (<c>dedup.unmerge</c>). Never throws.</summary>
    public ValueTask UnmergeAsync(string eventId) => ActAsync(
        view => view.Rows.FirstOrDefault(r => IsEvent(r, eventId))?.Merge is not null,
        id => _engine.UnmergeEventAsync(eventId, id));

    /// <summary>"Not the same meeting": shows one member of the merged card as an event of its own (<c>dedup.separate</c>). Never throws.</summary>
    public ValueTask SeparateAsync(string eventId, string memberId) => SeparateAsync(eventId, [memberId]);

    public ValueTask SeparateAsync(string eventId, IReadOnlyList<string> memberIds)
    {
        ArgumentNullException.ThrowIfNull(memberIds);
        var wanted = memberIds.ToArray();
        return ActAsync(
            view => wanted.Length > 0 && view.Rows.FirstOrDefault(r => IsEvent(r, eventId))?.Merge is { } merge
                && wanted.All(m => merge.Members.Any(x => string.Equals(x.MemberId, m, StringComparison.Ordinal))),
            id => _engine.SeparateMembersAsync(eventId, wanted, id));
    }

    /// <summary>Hides the message.</summary>
    public void DismissNotice()
    {
        lock (_gate)
        {
            if (_notice is null)
            {
                return;
            }

            _notice = null;
            _noticeVersion++;
        }

        Refresh();
    }

    private static bool IsEvent(FlyoutRow row, string eventId) => string.Equals(row.EventId, eventId, StringComparison.Ordinal);

    /// <summary>
    /// Runs a merge, unmerge or split. The card is checked against what the flyout shows now: an event that left the list, a merge that is
    /// no longer one, a member or a candidate the engine stopped offering all end in a short message and a fresh look at the engine's state,
    /// and nothing is sent. What is sent carries ids only (the engine resolves them from its latest state) and a command id, so a refusal
    /// from the engine comes back to <see cref="OnDiagnostic"/> as the same message.
    /// </summary>
    private async ValueTask ActAsync(Func<FlyoutView, bool> stillThere, Func<string, ValueTask> send)
    {
        string? id = null;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _notice = null;
            _noticeVersion++;
            if (stillThere(_view))
            {
                id = "flyout-merge-" + (++_commandCounter).ToString(CultureInfo.InvariantCulture);
                _pending.Add(id);
                if (_pending.Count > MaxPendingCommands)
                {
                    _pending.RemoveAt(0);
                }
            }
            else
            {
                SetNoticeLocked("Flyout_Merge_Stale");
            }
        }

        if (id is not null)
        {
            try
            {
                await send(id);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The engine is gone or refused to take the command.
                lock (_gate)
                {
                    _pending.Remove(id);
                    SetNoticeLocked("Flyout_Merge_Failed");
                }
            }
        }

        Refresh();
    }

    /// <summary>Shows a message and starts the timer that takes it away; a newer message is not taken away by an older timer.</summary>
    private void SetNoticeLocked(string key)
    {
        _notice = _text.GetString(key);
        var version = ++_noticeVersion;
        _ = ClearNoticeLaterAsync(version);
    }

    private async Task ClearNoticeLaterAsync(int version)
    {
        await Task.Delay(NoticeDuration, _time).ConfigureAwait(false);
        lock (_gate)
        {
            if (_disposed || _noticeVersion != version)
            {
                return;
            }

            _notice = null;
        }

        Refresh();
    }

    /// <summary>The engine refused a merge command of ours: the same message as for a card that went stale.</summary>
    private void OnDiagnostic(EngineDiagnosticInfo info)
    {
        if (info.CommandId is null)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed || !_pending.Remove(info.CommandId))
            {
                return;
            }

            SetNoticeLocked("Flyout_Merge_Stale");
        }

        Refresh();
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
        _diagnosticSubscription.Dispose();
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
            hasState && rows.Length > 0 && left == 0,
            _notice);
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
            failedUrl is null ? null : _text.GetString("Flyout_JoinFailed"))
        {
            Merge = mergeSummary is null ? null : BuildMerge(item, mergeSummary, zone),
            Candidates = new ValueList<CandidateRow>(item.Candidates.Select(c => BuildCandidate(c, zone))),
        };
    }

    private MergeInfo BuildMerge(AgendaItem item, string summary, TimeZoneInfo zone)
    {
        var all = item.Members.SelectMany(m => m.Calendars).ToList();
        var members = item.Members.Select(m => new MergeMemberRow(
            m.MemberId,
            string.Join(_text.GetString("Flyout_MetaSeparator"), CalendarLabel(m.Calendars, all), _words.Range(m.Start, m.End, zone)),
            Format("Flyout_NotSameMeetingAutomation", string.Join(", ", m.Calendars.Select(c => c.Title)), _words.SpokenRange(m.Start, m.End, zone))));
        return new MergeInfo(
            summary,
            MergeBadge.Text(item.MergeKind, item.MergeEngineName, _text, _culture),
            CalendarLabel(all, all),
            new ValueList<MergeMemberRow>(members));
    }

    private CandidateRow BuildCandidate(MergeCandidate candidate, TimeZoneInfo zone)
    {
        var when = candidate.IsAllDay ? _text.GetString("Flyout_AllDay") : _words.Clock(candidate.Start, zone);
        var automation = candidate.IsAllDay
            ? Format("Flyout_MergeWithAutomationAllDay", candidate.Title)
            : Format(
                "Flyout_MergeWithAutomation",
                candidate.Title,
                string.Join(_text.GetString("Flyout_Spoken_Separator"), new[] { candidate.CalendarTitle is null ? null : Format("Flyout_Spoken_Calendar", candidate.CalendarTitle), when }.OfType<string>()));
        return new CandidateRow(candidate.EventId, Format("Flyout_MergeWithItem", candidate.Title, when), automation);
    }

    /// <summary>
    /// Calendar titles in first-appearance order: "Work, Team". The flyout is narrow, so the account is added only to a title that more than
    /// one calendar of the card shares ("Calendar · Acme, Calendar · Home"); the Mac always writes it (<c>MergedMemberRow.label</c>).
    /// </summary>
    private string CalendarLabel(IEnumerable<MergeCalendar> calendars, IReadOnlyCollection<MergeCalendar> all)
    {
        var shared = all.GroupBy(c => c.Title, StringComparer.Ordinal).Where(g => g.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var titles = new List<string>();
        var accounts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var calendar in calendars)
        {
            if (!accounts.TryGetValue(calendar.Title, out var list))
            {
                titles.Add(calendar.Title);
                accounts[calendar.Title] = list = [];
            }

            if (shared.Contains(calendar.Title) && !string.IsNullOrWhiteSpace(calendar.AccountName) && !list.Contains(calendar.AccountName, StringComparer.Ordinal))
            {
                list.Add(calendar.AccountName);
            }
        }

        var separator = _text.GetString("Flyout_MetaSeparator");
        return string.Join(", ", titles.Select(t => accounts[t].Count == 0 ? t : t + separator + string.Join(", ", accounts[t])));
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
        var label = MergeBadge.Text(item.MergeKind, item.MergeEngineName, _text, _culture);
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

        // Only an expired sign-in can be fixed from here, and only when the engine says which account it is. Any other problem of a known
        // account can be looked at in the Accounts pane (its status, Sign in again, Remove).
        var canSignIn = problem is { Kind: ProblemKind.AuthExpired, ConnectionId: not null };
        var opensAccounts = !canSignIn && problem.ConnectionId is not null;
        return new ProblemRow(
            problem.SourceId,
            problem.Kind,
            message,
            canSignIn ? _text.GetString("Flyout_SignInAgain") : opensAccounts ? _text.GetString("Flyout_OpenAccounts") : null,
            canSignIn ? Format("Flyout_SignInAgainAutomation", problem.DisplayName) : opensAccounts ? Format("Flyout_OpenAccountsAutomation", problem.DisplayName) : null,
            problem.ConnectionId)
        {
            OpensAccounts = opensAccounts,
        };
    }

    private string Format(string key, params object[] arguments) => string.Format(_culture, _text.GetString(key), arguments);

    private sealed record FlyoutView(
        string Weekday,
        string Summary,
        IReadOnlyList<FlyoutRow> Rows,
        IReadOnlyList<ProblemRow> Problems,
        string Footer,
        bool IsEmpty,
        bool NoMoreMeetings,
        string? Notice)
    {
        public bool SameAs(FlyoutView other) =>
            Weekday == other.Weekday && Summary == other.Summary && Footer == other.Footer && IsEmpty == other.IsEmpty
            && NoMoreMeetings == other.NoMoreMeetings && Notice == other.Notice && Rows.SequenceEqual(other.Rows) && Problems.SequenceEqual(other.Problems);
    }

    private sealed class DiagnosticObserver(Action<EngineDiagnosticInfo> onNext) : IObserver<EngineDiagnosticInfo>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(EngineDiagnosticInfo value) => onNext(value);
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
