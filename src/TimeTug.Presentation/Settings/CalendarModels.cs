namespace TimeTug.Presentation.Settings;

/// <summary>Where a section of calendars comes from.</summary>
public enum CalendarOrigin
{
    /// <summary>A connected account (Google, Microsoft, iCloud, CalDAV, an iCal link).</summary>
    Account,

    /// <summary>A calendar source built into the operating system (the Mac's Apple Calendar). The Windows engine registers none in 1.0.</summary>
    System,

    /// <summary>A source that is no longer connected but still has calendars.</summary>
    Other,
}

/// <summary>
/// One calendar of the Calendars pane. <see cref="IsShown"/> is the visibility choice (not in the hidden list); <see cref="Tugs"/> is whether
/// its key is in the tug allow-list (as on the Mac, whether the calendar is also shown is a separate fact: a hidden calendar never tugs).
/// </summary>
/// <param name="Kind">The engine's kind: <c>standard</c>, <c>subscribed</c> or <c>birthdays</c>.</param>
/// <param name="IsSubscribed">A subscribed feed (kind <c>subscribed</c>).</param>
/// <param name="IsSystem">Set apart in the "subscribed and system calendars" disclosure: not a standard calendar, and Tug is off for it.</param>
/// <param name="AccountName">The account the engine says it belongs to (used for sub-groups and as a search keyword); null when it says none.</param>
public sealed record CalendarEntry(string Key, string Title, string? ColorHex, bool IsShown, bool Tugs, string? Kind, bool IsSubscribed, bool IsSystem, string? AccountName = null);

/// <summary>
/// One group of calendars in the Calendars pane: the Mac's <c>CalendarSection</c> and its account sub-groups flattened. A section (one source)
/// has one group per account name the engine reports for its calendars, and every group of a section repeats the section's
/// <see cref="SourceId"/>, <see cref="Title"/> and <see cref="Subtitle"/>. A connected account is one thing, so it has one group; a
/// system source can hold several accounts and names them (<see cref="ShowsGroupName"/>).
/// </summary>
/// <param name="AccountName">The group's account name ("Other" when the engine gave none).</param>
/// <param name="SourceId">The engine's source id (<c>&lt;kind&gt;-&lt;connection&gt;</c> for an account).</param>
/// <param name="KindId">The account kind (<c>google</c>, <c>icloud</c>, ...); empty for a system or unconnected source.</param>
/// <param name="Subtitle">The section's second line, for example "me@example.com · connected directly".</param>
/// <param name="ShowsGroupName">True when the group names its account (only a system source does).</param>
public sealed record CalendarGroup(
    string AccountName,
    string SourceId,
    string KindId,
    CalendarOrigin Origin,
    string? Subtitle,
    bool ShowsGroupName,
    IReadOnlyList<CalendarEntry> Calendars)
{
    /// <summary>The section's title: the provider's name ("Google"), "Other", or the system source's name.</summary>
    public string Title { get; init; } = string.Empty;
}

/// <summary>What the Calendars pane lists: sections of everyday calendars, and the subscribed and system-style ones apart.</summary>
public sealed record CalendarLayout(IReadOnlyList<CalendarGroup> Groups, IReadOnlyList<CalendarEntry> System)
{
    public static CalendarLayout Empty { get; } = new([], []);
}
