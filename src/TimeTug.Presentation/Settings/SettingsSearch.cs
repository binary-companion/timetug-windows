namespace TimeTug.Presentation.Settings;

/// <summary>One thing the search can find: a control (or a calendar) with the words that find it and the pane it lives in.</summary>
public sealed record SettingsSearchItem(string Id, string Title, IReadOnlyList<string> Keywords, SettingsPane Pane);

/// <summary>
/// The Mac's settings search (<c>SettingsSearch.swift</c>), ported. The catalog is every control's id, title, keywords and pane, plus one
/// entry per calendar (id <see cref="CalendarIdPrefix"/> + the calendar's key). The ids are the Mac's where the control is the same; the
/// differences on Windows are recorded in <c>docs/parity.md</c>: no Software Update entries (the Store updates the app), no Apple Calendar
/// words, the tray icon replaces the menu-bar text, the flyout backdrop replaces the popup card style, and About is an entry of its own.
/// Matching is the Mac's: the query is trimmed and folded (case- and diacritic-insensitive), a substring of the folded title is a title match, a
/// substring of a folded keyword is a keyword match, title matches come first, each group keeps catalog order, and an empty query finds nothing.
/// </summary>
public sealed class SettingsSearch
{
    public const string CalendarIdPrefix = "calendar:";

    private static readonly (string Id, SettingsPane Pane)[] Controls =
    [
        ("accounts", SettingsPane.Accounts),
        ("diagnostics", SettingsPane.Accounts),
        ("enable-tug", SettingsPane.TugRules),
        ("lead-time", SettingsPane.TugRules),
        ("video-link", SettingsPane.TugRules),
        ("require-attendees", SettingsPane.TugRules),
        ("test-takeover", SettingsPane.TugRules),
        ("skip-all-day", SettingsPane.Calendars),
        ("dedup-inference", SettingsPane.Calendars),
        ("appearance", SettingsPane.Appearance),
        ("popup-card-style", SettingsPane.Appearance),
        ("tray-icon-style", SettingsPane.Appearance),
        ("launch-at-login", SettingsPane.General),
        ("popup-shortcut", SettingsPane.General),
        ("about", SettingsPane.General),
    ];

    private readonly IReadOnlyList<SettingsSearchItem> _catalog;

    public SettingsSearch(SettingsText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _catalog = [.. Controls.Select(c => new SettingsSearchItem(c.Id, text.ControlTitle(c.Id), text.Keywords(c.Id), c.Pane))];
    }

    /// <summary>Every control, in the order results list them.</summary>
    public IReadOnlyList<SettingsSearchItem> Catalog => _catalog;

    /// <summary>The controls (and the calendars given) that match <paramref name="query"/>: title matches first, then keyword matches.</summary>
    public IReadOnlyList<SettingsSearchItem> Results(string query, IEnumerable<CalendarEntry> calendars)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(calendars);
        var needle = TextFolding.Fold(query.Trim());
        if (needle.Length == 0)
        {
            return [];
        }

        var all = _catalog.Concat(calendars.Select(c => new SettingsSearchItem(
            CalendarIdPrefix + c.Key, c.Title, c.AccountName is null ? [] : [c.AccountName], SettingsPane.Calendars))).ToList();
        var folded = all.Select(i => TextFolding.Fold(i.Title)).ToList();
        var titleMatches = new List<SettingsSearchItem>();
        var keywordMatches = new List<SettingsSearchItem>();
        for (var i = 0; i < all.Count; i++)
        {
            if (folded[i].Contains(needle, StringComparison.Ordinal))
            {
                titleMatches.Add(all[i]);
            }
            else if (all[i].Keywords.Any(k => TextFolding.Fold(k).Contains(needle, StringComparison.Ordinal)))
            {
                keywordMatches.Add(all[i]);
            }
        }

        return [.. titleMatches, .. keywordMatches];
    }

    /// <summary>The General sub-page a control lives in, or null when it is on the hub or in another pane.</summary>
    public static GeneralDestination? DestinationFor(string id) => id switch
    {
        "about" => GeneralDestination.About,
        _ => null,
    };
}
