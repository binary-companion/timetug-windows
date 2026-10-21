using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using Pkg = global::TimeTug.Engine;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// The Calendars pane's sectioning rules, ported from the Mac's <c>CalendarSections.make</c> and <c>CalendarGrouping.groups</c> and kept in
/// this one place: order (a system source first, then connected accounts in the order they were connected, then sources that are no longer
/// connected), the titles and subtitles, which groups name their account, and which calendars are set apart as subscribed or system-style.
/// Pure: it only arranges what the engine's state holds.
/// </summary>
public static class CalendarSections
{
    /// <summary>The engine's id for the operating system's own calendar source (the Mac's Apple Calendar). The Windows engine registers none in 1.0.</summary>
    public const string SystemSourceId = "eventkit";

    private const string StandardKind = "standard";

    /// <summary>English text for each key, used when no localizer is supplied; a test keeps it equal to <c>Resources.resw</c>.</summary>
    internal static IReadOnlyDictionary<string, string> EnglishText { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Calendars_SubtitleDirect"] = "{0} · connected directly",
        ["Calendars_TitleOther"] = "Other",
        ["Calendars_SubtitleOther"] = "Sources that are no longer connected",
        ["Calendars_TitleSystem"] = "This PC",
        ["Calendars_SubtitleSystem"] = "Calendars on this PC",
        ["Calendars_AccountFallback"] = "Other",
        ["CalendarProvider_google"] = "Google",
        ["CalendarProvider_microsoft"] = "Microsoft",
        ["CalendarProvider_icloud"] = "iCloud",
        ["CalendarProvider_caldav"] = "CalDAV",
        ["CalendarProvider_icalsub"] = "iCal link",
    };

    /// <summary>
    /// "google" gives "Google"; names with their own capitalisation are spelled out (iCloud, CalDAV, iCal link); any other kind is capitalised.
    /// </summary>
    public static string ProviderName(string kindId, ILocalizer? text)
    {
        ArgumentNullException.ThrowIfNull(kindId);
        var key = "CalendarProvider_" + kindId;
        if (kindId.Length > 0 && (text?.TryGetString(key) ?? EnglishText.GetValueOrDefault(key)) is { } known)
        {
            return known;
        }

        return kindId.Length == 0 ? kindId : char.ToUpperInvariant(kindId[0]) + kindId[1..];
    }

    public static CalendarLayout Make(
        IReadOnlyList<Pkg.CalendarInfo> calendars,
        IReadOnlyList<AccountInfo> accounts,
        EngineSettingsView settings,
        ILocalizer? text)
    {
        ArgumentNullException.ThrowIfNull(calendars);
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(settings);
        var tug = settings.TakeoverCalendarKeys;

        // A calendar that tugs always stays in the main list; non-standard calendars (birthdays, subscribed feeds) that do not tug are set apart.
        var apart = calendars.Where(c => KindOf(c) != StandardKind && !tug.Contains(c.Key)).ToList();
        var apartKeys = apart.Select(c => c.Key).ToHashSet(StringComparer.Ordinal);
        var main = calendars.Where(c => !apartKeys.Contains(c.Key)).ToList();

        var groups = new List<CalendarGroup>();
        var known = new HashSet<string>(StringComparer.Ordinal) { SystemSourceId };

        void Add(string sourceId, CalendarOrigin origin, string kindId, string title, string subtitle)
        {
            var own = main.Where(c => c.SourceID == sourceId).ToList();
            if (own.Count > 0)
            {
                groups.AddRange(GroupByAccount(own, sourceId, origin, kindId, title, subtitle, settings, text));
            }
        }

        Add(SystemSourceId, CalendarOrigin.System, string.Empty, Get(text, "Calendars_TitleSystem"), Get(text, "Calendars_SubtitleSystem"));
        foreach (var account in accounts)
        {
            var sourceId = $"{account.KindId}-{account.ConnectionId}";
            if (known.Add(sourceId))
            {
                Add(sourceId, CalendarOrigin.Account, account.KindId, ProviderName(account.KindId, text), Get(text, "Calendars_SubtitleDirect", account.DisplayName));
            }
        }

        foreach (var sourceId in main.Select(c => c.SourceID).Where(id => !known.Contains(id)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            Add(sourceId, CalendarOrigin.Other, string.Empty, Get(text, "Calendars_TitleOther"), Get(text, "Calendars_SubtitleOther"));
        }

        return new CalendarLayout(groups, apart.Select(c => ToEntry(c, settings, isSystem: true)).ToList());
    }

    /// <summary>One group per account name, in first-seen order, with the calendars of each sorted by title (the Mac's <c>CalendarGrouping.groups</c>).</summary>
    private static IEnumerable<CalendarGroup> GroupByAccount(
        List<Pkg.CalendarInfo> own, string sourceId, CalendarOrigin origin, string kindId, string title, string subtitle, EngineSettingsView settings, ILocalizer? text)
    {
        var fallback = Get(text, "Calendars_AccountFallback");
        var order = new List<string>();
        var buckets = new Dictionary<string, List<Pkg.CalendarInfo>>(StringComparer.Ordinal);
        foreach (var calendar in own)
        {
            var account = string.IsNullOrWhiteSpace(calendar.AccountName) ? fallback : calendar.AccountName.Trim();
            if (!buckets.TryGetValue(account, out var bucket))
            {
                buckets[account] = bucket = [];
                order.Add(account);
            }

            bucket.Add(calendar);
        }

        foreach (var account in order)
        {
            var entries = buckets[account]
                .Select(c => ToEntry(c, settings, isSystem: false))
                .OrderBy(e => e.Title, TextFolding.NaturalTitleOrder)
                .ToList();
            yield return new CalendarGroup(account, sourceId, kindId, origin, subtitle, ShowsGroupName: origin == CalendarOrigin.System, entries) { Title = title };
        }
    }

    private static CalendarEntry ToEntry(Pkg.CalendarInfo calendar, EngineSettingsView settings, bool isSystem)
    {
        var kind = calendar.Kind;
        return new CalendarEntry(
            calendar.Key,
            string.IsNullOrWhiteSpace(calendar.Title) ? calendar.Key : calendar.Title,
            string.IsNullOrWhiteSpace(calendar.ColorHex) ? null : calendar.ColorHex,
            IsShown: !settings.HiddenCalendarKeys.Contains(calendar.Key),
            Tugs: settings.TakeoverCalendarKeys.Contains(calendar.Key),
            kind,
            IsSubscribed: kind == "subscribed",
            IsSystem: isSystem,
            string.IsNullOrWhiteSpace(calendar.AccountName) ? null : calendar.AccountName.Trim());
    }

    private static string KindOf(Pkg.CalendarInfo calendar) => string.IsNullOrEmpty(calendar.Kind) ? StandardKind : calendar.Kind;

    private static string Get(ILocalizer? text, string key, params object[] arguments)
    {
        var format = text?.TryGetString(key) ?? EnglishText[key];
        return arguments.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.CurrentCulture, format, arguments);
    }
}
