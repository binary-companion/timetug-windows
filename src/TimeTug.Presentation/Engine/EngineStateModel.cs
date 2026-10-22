using TimeTug.Presentation.Settings;
using Pkg = global::TimeTug.Engine;

namespace TimeTug.Presentation.Engine;

/// <summary>A connected account. <see cref="Status"/> is <c>ok</c>, <c>needsPermission</c>, <c>authExpired</c> or <c>failing</c>.</summary>
public sealed record AccountInfo(string ConnectionId, string KindId, string DisplayName, string Status, int CalendarCount)
{
    /// <summary>True when the engine could not even start this account (a build failure, reported as <c>failing</c> in <see cref="Status"/>).</summary>
    public bool BuildFailure { get; init; }

    /// <summary>False until the engine has reported a status for the account's source: the Mac shows "Connecting…" for that moment.</summary>
    public bool StatusKnown { get; init; } = true;

    /// <summary>How many repeating events from the account's source use a rule the engine cannot read (the Mac's warning icon).</summary>
    public int UnreadableRecurrences { get; init; }
}

/// <summary>A kind of account the engine can add on this platform.</summary>
/// <summary><see cref="Authorization"/> is <c>oauth</c> (the browser), <c>password</c> (a form in the app) or <c>system</c>; empty when unknown.</summary>
public sealed record AccountKindInfo(string Id, string DisplayName, bool IsConfigured, string Authorization = "");

/// <summary>What the engine is doing about accounts right now. <see cref="WaitingStyle"/> is <c>browser</c> or <c>inApp</c>. <see cref="ErrorMessage"/> is the engine's own text, shown only as detail.</summary>
public sealed record AccountActivity(bool IsWorking, string? ErrorMessage, string WaitingStyle);

/// <summary>One event as the tray and the held-tug rule need it. All-day events carry the local midnights of their dates.</summary>
public sealed record EventSummary(string Id, string Title, DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, bool HasJoinLink);

public sealed record NextMeetingInfo(string Title, DateTimeOffset Start, bool HasJoinLink);

/// <summary>The engine's own answer to "when is the next takeover"; never computed in C#.</summary>
public sealed record NextTakeoverInfo(string EventId, DateTimeOffset FireAt);

/// <summary>An engine diagnostic. <see cref="CommandId"/> is the id of the command it answers (a refusal of a patch the app sent), or null.</summary>
public sealed record EngineDiagnosticInfo(string Level, string Category, string Message)
{
    public string? CommandId { get; init; }
}

/// <summary>Where an agenda item stands today. It is the engine's own answer (<c>agenda.items[].state</c>), never recomputed from the clock.</summary>
public enum AgendaPhase
{
    Past,
    Current,
    Upcoming,
}

/// <summary>
/// One row of today's agenda, with the calendar's title and colour already looked up. <see cref="Start"/> and <see cref="End"/> are the
/// tug time and the end; an all-day event carries the local midnights of its dates there and the calendar dates themselves in
/// <see cref="StartDate"/> and <see cref="EndDate"/> (exclusive), so a consumer never has to convert an instant back to a date.
/// </summary>
public sealed record AgendaItem(
    string EventId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    AgendaPhase Phase,
    string? CalendarTitle,
    string? CalendarColorHex,
    Uri? JoinUrl,
    int OtherAttendeeCount,
    bool IsMerged,
    string? Location)
{
    /// <summary>The calendar dates of an all-day event (<see cref="EndDate"/> is exclusive); null for a timed event.</summary>
    public DateOnly? StartDate { get; init; }

    public DateOnly? EndDate { get; init; }

    /// <summary>Where the shown time range starts: a merged meeting shows its longer copy. Equals <see cref="Start"/> otherwise.</summary>
    public DateTimeOffset ShownStart { get; init; } = Start;

    /// <summary>True for the event the engine names as the next meeting (<c>agenda.nextEventID</c>).</summary>
    public bool IsNext { get; init; }

    /// <summary>How many distinct events were merged into this one (0 or 1 when it is not a merge).</summary>
    public int MergedEventCount { get; init; }

    /// <summary>How the merge was decided: <c>rule</c>, <c>inference</c> or <c>userConfirmed</c>; null when not merged.</summary>
    public string? MergeKind { get; init; }

    /// <summary>The name of the model that proposed an <c>inference</c> merge.</summary>
    public string? MergeEngineName { get; init; }
}

public enum ProblemKind
{
    NeedsPermission,
    AuthExpired,
    Failing,
}

/// <summary>A calendar source the engine cannot read right now. <see cref="ConnectionId"/> is null when no connected account owns the source.</summary>
public sealed record SourceProblem(string SourceId, string DisplayName, ProblemKind Kind, string? Detail, string? ConnectionId);

/// <summary>The Presentation-side projection of the engine state.</summary>
public sealed record AppState(
    IReadOnlyList<AccountInfo> Accounts,
    IReadOnlyList<AccountKindInfo> AvailableKinds,
    IReadOnlyList<EventSummary> Events,
    NextMeetingInfo? NextMeeting,
    NextTakeoverInfo? NextTakeover,
    bool TakeoverVisible,
    bool Enabled,
    AccountActivity? Activity = null)
{
    /// <summary>Today's agenda in the engine's order.</summary>
    public IReadOnlyList<AgendaItem> Agenda { get; init; } = [];

    /// <summary>Sources that need the person (sorted by source id).</summary>
    public IReadOnlyList<SourceProblem> Problems { get; init; } = [];

    /// <summary>How long before a meeting the Tug fires (the engine's setting).</summary>
    public TimeSpan LeadTime { get; init; }

    /// <summary>The engine's state revision; it rises with every state the engine publishes.</summary>
    public ulong Revision { get; init; }

    /// <summary>
    /// The engine's settings. Before the engine has published a state they are its defaults, and <see cref="Enabled"/> and
    /// <see cref="LeadTime"/> stay the single source for those two fields so a state built without settings agrees with itself.
    /// </summary>
    public EngineSettingsView Settings
    {
        get => field ?? EngineSettingsView.Defaults with { Enabled = Enabled, LeadTime = LeadTime };
        init;
    }

    /// <summary>The calendars the pane lists, in sections (the Mac's order: system source, accounts in connection order, then sources no longer connected).</summary>
    public IReadOnlyList<CalendarGroup> CalendarGroups { get; init; } = [];

    /// <summary>The subscribed and system-style calendars the pane sets apart in a disclosure (not tugging, not standard).</summary>
    public IReadOnlyList<CalendarEntry> SystemCalendars { get; init; } = [];

    /// <summary>Whether on-device duplicate finding can run, from the engine's status.</summary>
    public InferenceAvailability Inference { get; init; } = InferenceAvailability.Disabled;

    /// <summary>The engine's reason when <see cref="Inference"/> is <see cref="InferenceAvailability.Unavailable"/>; null otherwise.</summary>
    public string? InferenceReason { get; init; }

    /// <summary>The name of the engine that runs the inference when <see cref="Inference"/> is <see cref="InferenceAvailability.Active"/>.</summary>
    public string? InferenceEngineName { get; init; }
}

/// <summary>A takeover the engine says is due.</summary>
public sealed record TakeoverRequest(
    string EventId,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    bool HasStarted,
    string? JoinUrl,
    IReadOnlyList<TimeSpan> SnoozeOptions,
    string? CalendarTitle,
    int OtherAttendeeCount);

/// <summary>Maps the engine package's records to the Presentation types. Pure: no rules, no clock.</summary>
public static class EngineMapping
{
    /// <param name="state">The engine's state.</param>
    /// <param name="text">The app's strings, for the calendar section titles; English when null.</param>
    public static AppState ToAppState(Pkg.EngineState state, Abstractions.ILocalizer? text = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var events = state.Events.Select(ToSummary).ToList();
        var next = state.Agenda.NextEventID is { } id ? events.FirstOrDefault(e => e.Id == id) : null;
        var accounts = state.Accounts.Select(account => ToAccount(account, state)).ToList();
        var settings = EngineSettingsMapping.ToView(state.Settings);
        var layout = CalendarSections.Make(state.Calendars, accounts, settings, text);
        return new AppState(
            accounts,
            state.AvailableAccountKinds.Select(k => new AccountKindInfo(k.Id, k.DisplayName, k.IsConfigured, k.Authorization ?? "")).ToList(),
            events,
            next is null ? null : new NextMeetingInfo(next.Title, next.Start, next.HasJoinLink),
            state.NextTakeover is { } t ? new NextTakeoverInfo(t.EventID, t.FireAt) : null,
            state.TakeoverVisible,
            state.Settings.Takeover.Enabled,
            new AccountActivity(
                state.AccountStatus.IsWorking,
                string.IsNullOrWhiteSpace(state.AccountStatus.ErrorMessage) ? null : state.AccountStatus.ErrorMessage,
                state.AccountStatus.WaitingStyle ?? "browser"))
        {
            Agenda = ToAgenda(state),
            Problems = ToProblems(state),
            LeadTime = settings.LeadTime,
            Revision = state.Revision,
            Settings = settings,
            CalendarGroups = layout.Groups,
            SystemCalendars = layout.System,
            Inference = EngineSettingsMapping.ToAvailability(state.InferenceStatus),
            InferenceReason = NullIfBlank(state.InferenceStatus?.Reason),
            InferenceEngineName = NullIfBlank(state.InferenceStatus?.EngineName),
        };
    }

    public static TakeoverRequest ToRequest(Pkg.TakeoverRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var summary = ToSummary(request.Event);
        return new TakeoverRequest(
            summary.Id,
            summary.Title,
            summary.Start,
            summary.End,
            summary.IsAllDay,
            request.HasStarted,
            request.JoinURL,
            request.SnoozeOptions.Select(TimeSpan.FromSeconds).ToList(),
            request.CalendarTitle,
            request.Event.OtherAttendeeCount);
    }

    public static EventSummary ToSummary(Pkg.CalendarEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var hasLink = e.Conferences.Count > 0;
        if (IsDated(e))
        {
            var zone = ZoneOf(e.TimeZone);
            var (startDate, endDate) = DatesOf(e);
            return new EventSummary(e.Id, e.Title, Midnight(startDate, zone), Midnight(endDate, zone), true, hasLink);
        }

        return new EventSummary(e.Id, e.Title, e.Start!.Value, e.End!.Value, false, hasLink);
    }

    /// <summary>An all-day event, or one the engine sent without instants: it is read by its dates.</summary>
    private static bool IsDated(Pkg.CalendarEvent e) => e.IsAllDay || e.Start is null || e.End is null;

    /// <summary>The calendar dates of a dated event; <c>End</c> is exclusive.</summary>
    private static (DateOnly Start, DateOnly End) DatesOf(Pkg.CalendarEvent e)
    {
        var startDate = e.StartDate ?? DateOnly.FromDateTime((e.Start ?? default).UtcDateTime);
        return (startDate, e.EndDate ?? startDate.AddDays(1));
    }

    // ---- today's agenda, the sources that need the person, the lead time ------------------------------------------

    /// <summary>
    /// One <see cref="AgendaItem"/> per engine agenda item, in the engine's order. The phase is the engine's word
    /// (<c>agenda.items[].state</c>); this method never compares a time with a clock.
    /// </summary>
    private static List<AgendaItem> ToAgenda(Pkg.EngineState state)
    {
        var events = new Dictionary<string, Pkg.CalendarEvent>(StringComparer.Ordinal);
        foreach (var e in state.Events)
        {
            events.TryAdd(e.Id, e);
        }

        var calendars = new Dictionary<string, Pkg.CalendarInfo>(StringComparer.Ordinal);
        foreach (var c in state.Calendars)
        {
            calendars.TryAdd(c.Key, c);
        }

        var items = new List<AgendaItem>(state.Agenda.Items.Count);
        foreach (var entry in state.Agenda.Items)
        {
            if (events.TryGetValue(entry.EventID, out var e))
            {
                calendars.TryGetValue(e.CalendarKey, out var calendar);
                items.Add(ToAgendaItem(e, ToPhase(entry.State), calendar, entry.EventID == state.Agenda.NextEventID));
            }
        }

        return items;
    }

    private static AgendaItem ToAgendaItem(Pkg.CalendarEvent e, AgendaPhase phase, Pkg.CalendarInfo? calendar, bool isNext)
    {
        var summary = ToSummary(e);
        var distinct = e.Members.Select(m => m.MemberID).Distinct(StringComparer.Ordinal).Count();
        var merged = distinct > 1;
        (DateOnly Start, DateOnly End)? dates = IsDated(e) ? DatesOf(e) : null;
        return new AgendaItem(
            e.Id,
            e.Title,
            summary.Start,
            summary.End,
            summary.IsAllDay,
            phase,
            calendar?.Title,
            string.IsNullOrWhiteSpace(calendar?.ColorHex) ? null : calendar.ColorHex,
            JoinUrlOf(e),
            e.OtherAttendeeCount,
            merged,
            string.IsNullOrWhiteSpace(e.Location) ? null : e.Location)
        {
            StartDate = dates?.Start,
            EndDate = dates?.End,
            ShownStart = e.DisplayStart ?? summary.Start,
            IsNext = isNext,
            MergedEventCount = distinct,
            MergeKind = merged ? e.MergeProvenance?.Kind : null,
            MergeEngineName = merged ? e.MergeProvenance?.EngineName : null,
        };
    }

    /// <summary>The engine's words for a phase; a word a later engine adds is shown as upcoming rather than hiding the row.</summary>
    private static AgendaPhase ToPhase(string state) => state switch
    {
        "past" => AgendaPhase.Past,
        "current" => AgendaPhase.Current,
        _ => AgendaPhase.Upcoming,
    };

    /// <summary>The first conference link that is a web address; any other scheme (script, file, an app link) is not offered as Join.</summary>
    private static Uri? JoinUrlOf(Pkg.CalendarEvent e)
    {
        foreach (var conference in e.Conferences)
        {
            if (Uri.TryCreate(conference.Url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            {
                return uri;
            }
        }

        return null;
    }

    /// <summary>
    /// A problem per source whose status is not <c>ok</c>, sorted by source id. The engine names a connection's source
    /// <c>"&lt;kind&gt;-&lt;connection&gt;"</c> (calendar-connectors <c>Connection.sourceID</c>); that rule is how a source finds the account that
    /// owns it, so Reconnect has a connection id. A source no account owns (for example the system calendar) has none.
    /// </summary>
    private static List<SourceProblem> ToProblems(Pkg.EngineState state)
    {
        var problems = new List<SourceProblem>();
        foreach (var (sourceId, status) in state.Statuses)
        {
            if (status.Kind == "ok")
            {
                continue;
            }

            var connectionId = state.Accounts.FirstOrDefault(a => $"{a.KindID}-{a.ConnectionID}" == sourceId)?.ConnectionID;
            problems.Add(new SourceProblem(
                sourceId,
                state.SourceNames.TryGetValue(sourceId, out var name) ? name : sourceId,
                status.Kind switch
                {
                    "needsPermission" => ProblemKind.NeedsPermission,
                    "authExpired" => ProblemKind.AuthExpired,
                    _ => ProblemKind.Failing,
                },
                string.IsNullOrWhiteSpace(status.Message) ? null : status.Message,
                connectionId));
        }

        problems.Sort((a, b) => string.CompareOrdinal(a.SourceId, b.SourceId));
        return problems;
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

    private static AccountInfo ToAccount(Pkg.Account account, Pkg.EngineState state)
    {
        // The engine names a connection's source "<kind>-<connection>"; fall back to matching the display name.
        var sourceId = state.SourceNames.ContainsKey($"{account.KindID}-{account.ConnectionID}")
            ? $"{account.KindID}-{account.ConnectionID}"
            : state.SourceNames.Where(p => p.Value == account.DisplayName).Select(p => p.Key).FirstOrDefault();
        var buildFailure = state.AccountStatus.BuildFailures.ContainsKey(account.ConnectionID);
        var reported = sourceId is not null && state.Statuses.TryGetValue(sourceId, out var s) ? s.Kind : null;
        var status = buildFailure ? "failing" : reported ?? "ok";
        var calendars = sourceId is null ? 0 : state.Calendars.Count(c => c.SourceID == sourceId);
        var unreadable = sourceId is not null && state.Notices.TryGetValue(sourceId, out var notices)
            ? notices.Where(n => n.Kind == "unreadableRecurrence").Sum(n => n.Count)
            : 0;
        return new AccountInfo(account.ConnectionID, account.KindID, account.DisplayName, status, calendars)
        {
            BuildFailure = buildFailure,
            StatusKnown = buildFailure || reported is not null,
            UnreadableRecurrences = unreadable,
        };
    }

    private static TimeZoneInfo ZoneOf(string ianaId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
