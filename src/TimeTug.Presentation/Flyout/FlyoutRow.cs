using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Flyout;

/// <summary>How a card looks. It follows the Mac's popup: all-day first, then the engine's phase, then whether it is the next meeting.</summary>
public enum FlyoutRowKind
{
    AllDay,
    Past,
    Current,
    Next,
    Upcoming,
}

/// <summary>One event card in the flyout with all text decided. Equal rows are interchangeable, so the view model can tell when nothing changed.</summary>
/// <param name="EventId">The engine's event id.</param>
/// <param name="Kind">Past, current, next, upcoming or all-day (the style flag).</param>
/// <param name="TimeText">"7:15 PM", or "All day".</param>
/// <param name="Title">The event title.</param>
/// <param name="MetaText">The second line: "7:15 – 8:00 PM · Work", "Ends 4:00 PM", "30 min · done", "All day · Personal".</param>
/// <param name="CalendarTitle">The calendar's own title.</param>
/// <param name="CalendarColorHex">The calendar's colour, for the bar on the card; null when the calendar has none.</param>
/// <param name="AttendeeText">"5 others"; null with no other attendees.</param>
/// <param name="BadgeText">"Now" on the meeting in progress, "in 3h 40m" on the next one; null otherwise.</param>
/// <param name="Progress">How far through the meeting in progress we are (0 to 1); 0 for every other row.</param>
/// <param name="JoinUrl">The link to open; set only for the meeting in progress and the next one.</param>
/// <param name="JoinLabel">The provider's wording for a tooltip: "Join Zoom".</param>
/// <param name="JoinAutomationName">"Join Design review".</param>
/// <param name="MergeSummary">"Merged with Apple Intelligence · 4 events" or "4 events merged"; null when not a merge.</param>
/// <param name="AutomationName">What a screen reader says for the whole card.</param>
/// <param name="JoinFailedUrl">The link that could not be opened (so the person can copy it); null when Join has not failed.</param>
/// <param name="JoinFailedMessage">The sentence that goes with <paramref name="JoinFailedUrl"/>.</param>
public sealed record FlyoutRow(
    string EventId,
    FlyoutRowKind Kind,
    string TimeText,
    string Title,
    string MetaText,
    string? CalendarTitle,
    string? CalendarColorHex,
    string? AttendeeText,
    string? BadgeText,
    double Progress,
    Uri? JoinUrl,
    string? JoinLabel,
    string JoinAutomationName,
    string? MergeSummary,
    string AutomationName,
    string? JoinFailedUrl = null,
    string? JoinFailedMessage = null)
{
    public bool HasJoin => JoinUrl is not null;

    public bool IsPast => Kind == FlyoutRowKind.Past;

    public bool IsCurrent => Kind == FlyoutRowKind.Current;

    public bool IsNext => Kind == FlyoutRowKind.Next;

    public bool IsAllDay => Kind == FlyoutRowKind.AllDay;

    public bool HasJoinFailed => JoinFailedUrl is not null;
}

/// <summary>
/// A source the engine cannot read. <see cref="ActionText"/> is the button's label, or null when there is nothing the person can do here:
/// "Sign in again" for an expired sign-in, "Open Accounts" for any other problem of a known account.
/// </summary>
public sealed record ProblemRow(
    string SourceId,
    ProblemKind Kind,
    string Message,
    string? ActionText,
    string? ActionAutomationName,
    string? ConnectionId)
{
    /// <summary>True when the action opens the Settings window's Accounts pane (a problem the person cannot fix from the flyout) instead of signing in again.</summary>
    public bool OpensAccounts { get; init; }

    public bool HasAction => ActionText is not null;
}
