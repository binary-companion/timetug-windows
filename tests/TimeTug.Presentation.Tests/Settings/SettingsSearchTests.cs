using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>The Mac's settings search, ported: the catalog, the folded substring match, title matches before keyword matches.</summary>
public class SettingsSearchTests
{
    private static readonly SettingsText Text = new(new ReswLocalizer());
    private static readonly SettingsSearch Search = new(Text);

    private static CalendarEntry Calendar(string key, string title, string? account = null) =>
        new(key, title, null, IsShown: true, Tugs: false, "standard", false, false, account);

    private static string[] Ids(string query, params CalendarEntry[] calendars) => [.. Search.Results(query, calendars).Select(i => i.Id)];

    [Fact]
    public void An_empty_or_blank_query_finds_nothing()
    {
        Ids("").Should().BeEmpty();
        Ids("   ").Should().BeEmpty();
        Ids(" \t\n").Should().BeEmpty();
    }

    [Fact]
    public void A_control_is_found_by_its_title_and_by_a_keyword()
    {
        Ids("lead time").Should().Contain("lead-time");
        Ids("zoom").Should().Contain("video-link", "a keyword finds the control");
        Ids("zoom").Should().NotContain("lead-time");
    }

    [Fact]
    public void Matching_ignores_case_and_diacritics_on_both_sides()
    {
        Ids("LEAD TIME").Should().Contain("lead-time");
        Ids("  lead time  ").Should().Contain("lead-time", "the query is trimmed");
        Ids("cafe", Calendar("g/1", "Café Sync")).Should().Contain("calendar:g/1");
        Ids("CAFÉ", Calendar("g/1", "cafe sync")).Should().Contain("calendar:g/1");
        Ids("résumé", Calendar("g/2", "Resume review")).Should().Contain("calendar:g/2");
    }

    [Fact]
    public void Title_matches_come_before_keyword_matches_and_each_group_keeps_catalog_order()
    {
        var ids = Ids("tug");

        var titleMatches = Search.Catalog.Where(i => Fold(i.Title).Contains("tug")).Select(i => i.Id).ToArray();
        var keywordMatches = Search.Catalog.Where(i => !Fold(i.Title).Contains("tug") && i.Keywords.Any(k => Fold(k).Contains("tug"))).Select(i => i.Id).ToArray();

        titleMatches.Should().NotBeEmpty();
        keywordMatches.Should().NotBeEmpty();
        ids.Should().Equal(titleMatches.Concat(keywordMatches));
    }

    [Fact]
    public void A_title_that_matches_is_not_listed_again_as_a_keyword_match()
    {
        Ids("tug").Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Calendars_are_found_by_title_or_account_and_follow_the_catalog_entries()
    {
        var results = Search.Results("work", [Calendar("g/1", "Work", "me@example.com"), Calendar("g/2", "Family", "work@example.com")]);

        results.Select(i => i.Id).Should().Contain(["calendar:g/1", "calendar:g/2"]);
        results.Single(i => i.Id == "calendar:g/1").Pane.Should().Be(SettingsPane.Calendars);
        results.Select(i => i.Id).ToList().IndexOf("calendar:g/1").Should().BeLessThan(results.Select(i => i.Id).ToList().IndexOf("calendar:g/2"), "a title match comes before an account keyword match");
        results.Single(i => i.Id == "calendar:g/2").Keywords.Should().Equal("work@example.com");
        results.Select(i => i.Id).Where(id => !id.StartsWith(SettingsSearch.CalendarIdPrefix, StringComparison.Ordinal)).Should().Equal(Ids("work"));
    }

    [Fact]
    public void A_calendar_without_an_account_name_has_no_keywords()
    {
        Search.Results("solo", [Calendar("g/3", "Solo")]).Single(i => i.Id == "calendar:g/3").Keywords.Should().BeEmpty();
    }

    [Fact]
    public void There_is_no_software_update_entry_because_the_store_updates_the_app()
    {
        var ids = Search.Catalog.Select(i => i.Id).ToArray();
        ids.Should().NotContain(["software-update", "automatic-updates", "beta-updates"]);
        Search.Catalog.Select(i => i.Title).Should().NotContain(t => t.Contains("update", StringComparison.OrdinalIgnoreCase));
        Search.Catalog.SelectMany(i => i.Keywords).Should().NotContain(k => k.Contains("sparkle", StringComparison.OrdinalIgnoreCase));
        foreach (var query in new[] { "software update", "check for updates", "automatic updates", "beta updates", "sparkle", "prerelease" })
        {
            Ids(query).Should().BeEmpty("Windows has no in-app updater to search for: " + query);
        }
    }

    [Fact]
    public void Every_catalog_entry_has_a_unique_id_a_title_and_a_pane()
    {
        Search.Catalog.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        Search.Catalog.Should().OnlyContain(i => i.Title.Length > 0 && Enum.IsDefined(i.Pane));
        Search.Catalog.Select(i => i.Pane).Distinct().Should().BeEquivalentTo(Enum.GetValues<SettingsPane>(), "every pane has something to find");
    }

    [Fact]
    public void The_catalog_keeps_the_macs_controls_with_the_macs_ids_and_panes()
    {
        var expected = new Dictionary<string, SettingsPane>
        {
            ["accounts"] = SettingsPane.Accounts,
            ["diagnostics"] = SettingsPane.Accounts,
            ["enable-tug"] = SettingsPane.TugRules,
            ["lead-time"] = SettingsPane.TugRules,
            ["video-link"] = SettingsPane.TugRules,
            ["require-attendees"] = SettingsPane.TugRules,
            ["test-takeover"] = SettingsPane.TugRules,
            ["skip-all-day"] = SettingsPane.Calendars,
            ["dedup-inference"] = SettingsPane.Calendars,
            ["appearance"] = SettingsPane.Appearance,
            ["popup-card-style"] = SettingsPane.Appearance,
            ["launch-at-login"] = SettingsPane.General,
            ["popup-shortcut"] = SettingsPane.General,
        };

        foreach (var (id, pane) in expected)
        {
            Search.Catalog.Should().ContainSingle(i => i.Id == id).Which.Pane.Should().Be(pane, id);
        }
    }

    [Fact]
    public void Windows_only_entries_cover_where_windows_differs()
    {
        Search.Catalog.Should().NotContain(i => i.Id == "menu-bar-text");
        Search.Catalog.Single(i => i.Id == "tray-icon-style").Pane.Should().Be(SettingsPane.Appearance);
        Search.Catalog.Single(i => i.Id == "about").Pane.Should().Be(SettingsPane.General);
        Search.Catalog.Single(i => i.Id == "accounts").Keywords.Should().NotContain(["apple calendar", "eventkit"], "Apple Calendar is Mac-only");
        Ids("taskbar").Should().Contain("tray-icon-style");
        Ids("version").Should().Contain("about");
    }

    [Fact]
    public void Controls_inside_a_general_sub_page_name_the_page_to_open()
    {
        SettingsSearch.DestinationFor("about").Should().Be(GeneralDestination.About);
        SettingsSearch.DestinationFor("launch-at-login").Should().BeNull();
        SettingsSearch.DestinationFor("calendar:g/1").Should().BeNull();
    }

    private static string Fold(string text) => TextFolding.Fold(text);
}
