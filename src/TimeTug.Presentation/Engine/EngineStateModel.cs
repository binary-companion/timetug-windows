using Pkg = global::TimeTug.Engine;

namespace TimeTug.Presentation.Engine;

/// <summary>A connected account. <see cref="Status"/> is <c>ok</c>, <c>needsPermission</c>, <c>authExpired</c> or <c>failing</c>.</summary>
public sealed record AccountInfo(string ConnectionId, string KindId, string DisplayName, string Status, int CalendarCount);

/// <summary>A kind of account the engine can add on this platform.</summary>
public sealed record AccountKindInfo(string Id, string DisplayName, bool IsConfigured);

/// <summary>One event as the tray and the held-tug rule need it. All-day events carry the local midnights of their dates.</summary>
public sealed record EventSummary(string Id, string Title, DateTimeOffset Start, DateTimeOffset End, bool IsAllDay, bool HasJoinLink);

public sealed record NextMeetingInfo(string Title, DateTimeOffset Start, bool HasJoinLink);

/// <summary>The engine's own answer to "when is the next takeover"; never computed in C#.</summary>
public sealed record NextTakeoverInfo(string EventId, DateTimeOffset FireAt);

public sealed record EngineDiagnosticInfo(string Level, string Category, string Message);

/// <summary>The Presentation-side projection of the engine state.</summary>
public sealed record AppState(
    IReadOnlyList<AccountInfo> Accounts,
    IReadOnlyList<AccountKindInfo> AvailableKinds,
    IReadOnlyList<EventSummary> Events,
    NextMeetingInfo? NextMeeting,
    NextTakeoverInfo? NextTakeover,
    bool TakeoverVisible,
    bool Enabled);

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
    public static AppState ToAppState(Pkg.EngineState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var events = state.Events.Select(ToSummary).ToList();
        var next = state.Agenda.NextEventID is { } id ? events.FirstOrDefault(e => e.Id == id) : null;
        return new AppState(
            state.Accounts.Select(account => ToAccount(account, state)).ToList(),
            state.AvailableAccountKinds.Select(k => new AccountKindInfo(k.Id, k.DisplayName, k.IsConfigured)).ToList(),
            events,
            next is null ? null : new NextMeetingInfo(next.Title, next.Start, next.HasJoinLink),
            state.NextTakeover is { } t ? new NextTakeoverInfo(t.EventID, t.FireAt) : null,
            state.TakeoverVisible,
            state.Settings.Takeover.Enabled);
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
        if (e.IsAllDay || e.Start is null || e.End is null)
        {
            var zone = ZoneOf(e.TimeZone);
            var startDate = e.StartDate ?? DateOnly.FromDateTime((e.Start ?? default).UtcDateTime);
            var endDate = e.EndDate ?? startDate.AddDays(1);
            return new EventSummary(e.Id, e.Title, Midnight(startDate, zone), Midnight(endDate, zone), true, hasLink);
        }

        return new EventSummary(e.Id, e.Title, e.Start.Value, e.End.Value, false, hasLink);
    }

    private static AccountInfo ToAccount(Pkg.Account account, Pkg.EngineState state)
    {
        // The engine names a connection's source "<kind>-<connection>"; fall back to matching the display name.
        var sourceId = state.SourceNames.ContainsKey($"{account.KindID}-{account.ConnectionID}")
            ? $"{account.KindID}-{account.ConnectionID}"
            : state.SourceNames.Where(p => p.Value == account.DisplayName).Select(p => p.Key).FirstOrDefault();
        var status = state.AccountStatus.BuildFailures.ContainsKey(account.ConnectionID)
            ? "failing"
            : sourceId is not null && state.Statuses.TryGetValue(sourceId, out var s) ? s.Kind : "ok";
        var calendars = sourceId is null ? 0 : state.Calendars.Count(c => c.SourceID == sourceId);
        return new AccountInfo(account.ConnectionID, account.KindID, account.DisplayName, status, calendars);
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
