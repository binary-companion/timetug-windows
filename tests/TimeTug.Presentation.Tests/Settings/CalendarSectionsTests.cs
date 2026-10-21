using System.Text.Json;
using System.Text.Json.Nodes;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Welcome;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>The Mac's <c>CalendarSectionsTests</c>, ported, plus the Windows additions (the engine's state, hidden calendars, ordering).</summary>
public class CalendarSectionsTests
{
    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);
    private static readonly ReswLocalizer Text = new();
    private static readonly AccountInfo Google = new("g1", "google", "me@gmail.com", "ok", 1);

    private static Pkg.CalendarInfo Cal(string source, string id, string title, string? account = null, string kind = "standard", string? color = null) => new()
    {
        Key = $"{source}/{id}",
        SourceID = source,
        CalendarID = id,
        Title = title,
        AccountName = account,
        Kind = kind,
        ColorHex = color,
    };

    private static CalendarLayout Make(IReadOnlyList<Pkg.CalendarInfo> calendars, IEnumerable<string>? tug = null, IEnumerable<string>? hidden = null, params AccountInfo[] accounts) =>
        CalendarSections.Make(
            calendars,
            accounts.Length == 0 ? [Google] : accounts,
            EngineSettingsView.Defaults with
            {
                TakeoverCalendarKeys = new HashSet<string>(tug ?? []),
                HiddenCalendarKeys = new HashSet<string>(hidden ?? []),
            },
            Text);

    private static IEnumerable<string> Titles(CalendarLayout layout) => layout.Groups.Select(g => g.Title).Distinct();

    [Fact]
    public void The_system_source_comes_first_then_accounts_and_each_is_labelled()
    {
        var layout = Make([Cal("google-g1", "1", "Mine"), Cal("eventkit", "2", "Home", "iCloud")]);

        Titles(layout).Should().Equal("This PC", "Google");
        layout.Groups.Select(g => g.Subtitle).Should().Equal("Calendars on this PC", "me@gmail.com · connected directly");
        layout.Groups.Select(g => g.Origin).Should().Equal(CalendarOrigin.System, CalendarOrigin.Account);
        layout.Groups.Select(g => g.KindId).Should().Equal(string.Empty, "google");
        layout.Groups.Select(g => g.SourceId).Should().Equal("eventkit", "google-g1");
    }

    [Fact]
    public void The_system_source_is_sub_grouped_by_its_accounts_but_direct_accounts_are_not()
    {
        var layout = Make([
            Cal("eventkit", "1", "A", "Exchange"),
            Cal("eventkit", "2", "B", "Gmail"),
            Cal("google-g1", "3", "C", "me@gmail.com"),
        ]);

        layout.Groups.Select(g => g.AccountName).Should().Equal("Exchange", "Gmail", "me@gmail.com");
        layout.Groups.Select(g => g.ShowsGroupName).Should().Equal(true, true, false);
        layout.Groups.Select(g => g.SourceId).Should().Equal("eventkit", "eventkit", "google-g1");
    }

    [Fact]
    public void Birthdays_and_subscriptions_move_to_the_system_list_unless_tug_is_on()
    {
        var layout = Make(
            [
                Cal("eventkit", "1", "Work"),
                Cal("eventkit", "2", "Birthdays", kind: "birthdays"),
                Cal("eventkit", "3", "Holidays", kind: "subscribed"),
                Cal("eventkit", "4", "Team Feed", kind: "subscribed"),
            ],
            tug: ["eventkit/4"]);

        layout.Groups.SelectMany(g => g.Calendars).Select(c => c.Title).Should().Equal("Team Feed", "Work");
        layout.System.Select(c => c.Title).Should().Equal("Birthdays", "Holidays");
        layout.System.Should().OnlyContain(c => c.IsSystem);
        layout.Groups.SelectMany(g => g.Calendars).Should().OnlyContain(c => !c.IsSystem);
        layout.System.Single(c => c.Title == "Holidays").IsSubscribed.Should().BeTrue();
        layout.System.Single(c => c.Title == "Birthdays").IsSubscribed.Should().BeFalse();
    }

    [Fact]
    public void A_hidden_subscribed_calendar_that_tugs_stays_in_the_main_list()
    {
        var layout = Make([Cal("google-g1", "1", "Feed", kind: "subscribed")], tug: ["google-g1/1"], hidden: ["google-g1/1"]);

        layout.System.Should().BeEmpty("a calendar that tugs always stays in the main list");
        var entry = layout.Groups.Single().Calendars.Single();
        entry.IsShown.Should().BeFalse();
        entry.Tugs.Should().BeTrue();
        entry.IsSubscribed.Should().BeTrue();
    }

    [Fact]
    public void Sections_with_no_main_calendars_are_dropped()
    {
        var layout = Make([Cal("eventkit", "1", "Birthdays", kind: "birthdays")]);

        layout.Groups.Should().BeEmpty();
        layout.System.Should().HaveCount(1);
    }

    [Fact]
    public void Calendars_of_an_unknown_source_go_to_other()
    {
        var layout = Make([Cal("gone", "1", "Orphan")]);

        var group = layout.Groups.Should().ContainSingle().Subject;
        group.Title.Should().Be("Other");
        group.Subtitle.Should().Be("Sources that are no longer connected");
        group.Origin.Should().Be(CalendarOrigin.Other);
        group.KindId.Should().BeEmpty();
        group.SourceId.Should().Be("gone");
    }

    [Fact]
    public void Several_unknown_sources_are_listed_by_source_id_after_the_accounts()
    {
        var layout = Make([Cal("zeta", "1", "Z"), Cal("alpha", "1", "A"), Cal("google-g1", "1", "G")]);

        layout.Groups.Select(g => g.SourceId).Should().Equal("google-g1", "alpha", "zeta");
    }

    [Fact]
    public void Accounts_follow_the_order_they_were_connected_and_each_has_its_own_subtitle()
    {
        var work = new AccountInfo("g2", "google", "me@work.example", "ok", 1);
        var home = new AccountInfo("i1", "icloud", "me@icloud.com", "ok", 1);

        var layout = Make(
            [Cal("icloud-i1", "1", "Family", "me@icloud.com"), Cal("google-g2", "1", "Work", "me@work.example"), Cal("google-g1", "1", "Mine", "me@gmail.com")],
            accounts: [Google, work, home]);

        layout.Groups.Select(g => (g.Title, g.Subtitle)).Should().Equal(
            ("Google", "me@gmail.com · connected directly"),
            ("Google", "me@work.example · connected directly"),
            ("iCloud", "me@icloud.com · connected directly"));
    }

    [Fact]
    public void An_account_with_no_calendars_has_no_section()
    {
        Make([]).Groups.Should().BeEmpty();
        Make([Cal("caldav-9", "1", "Work")]).Groups.Single().Origin.Should().Be(CalendarOrigin.Other);
    }

    [Fact]
    public void Calendars_in_a_group_are_sorted_by_title_the_way_a_person_counts()
    {
        var layout = Make([
            Cal("google-g1", "1", "Team 10", "me@gmail.com"),
            Cal("google-g1", "2", "team 2", "me@gmail.com"),
            Cal("google-g1", "3", "Équipe", "me@gmail.com"),
            Cal("google-g1", "4", "Team 1", "me@gmail.com"),
            Cal("google-g1", "5", "Zebra", "me@gmail.com"),
        ]);

        layout.Groups.Single().Calendars.Select(c => c.Title).Should().Equal("Équipe", "Team 1", "team 2", "Team 10", "Zebra");
    }

    [Fact]
    public void A_calendar_without_an_account_name_is_grouped_under_other()
    {
        var layout = Make([Cal("google-g1", "1", "A", account: null), Cal("google-g1", "2", "B", account: "   ")]);

        layout.Groups.Should().ContainSingle().Which.AccountName.Should().Be("Other");
    }

    [Fact]
    public void Entries_carry_the_flags_the_pane_needs()
    {
        var layout = Make(
            [Cal("google-g1", "a", "Work", "me@gmail.com", color: "#3366CC"), Cal("google-g1", "b", "Quiet", "me@gmail.com")],
            tug: ["google-g1/a"],
            hidden: ["google-g1/b"]);

        var entries = layout.Groups.Single().Calendars;
        entries.Should().Equal(
            new CalendarEntry("google-g1/b", "Quiet", null, IsShown: false, Tugs: false, "standard", false, false, "me@gmail.com"),
            new CalendarEntry("google-g1/a", "Work", "#3366CC", IsShown: true, Tugs: true, "standard", false, false, "me@gmail.com"));
    }

    [Theory]
    [InlineData("google", "Google")]
    [InlineData("microsoft", "Microsoft")]
    [InlineData("icloud", "iCloud")]
    [InlineData("caldav", "CalDAV")]
    [InlineData("icalsub", "iCal link")]
    [InlineData("zoom", "Zoom")]
    [InlineData("", "")]
    public void Provider_names_spell_out_the_ones_with_their_own_capitalisation(string kindId, string expected)
    {
        CalendarSections.ProviderName(kindId, Text).Should().Be(expected);
    }

    [Fact]
    public void Without_a_localizer_the_text_is_the_same_English_the_resource_file_holds()
    {
        IReadOnlyList<Pkg.CalendarInfo> calendars =
        [
            Cal("eventkit", "1", "Home", "iCloud"),
            Cal("google-g1", "2", "Mine", "me@gmail.com"),
            Cal("gone", "3", "Orphan"),
            Cal("google-g1", "4", "Blank", " "),
        ];
        var accounts = new[] { Google, new AccountInfo("c1", "caldav", "Work", "ok", 0), new AccountInfo("f1", "icalsub", "Feed", "ok", 0), new AccountInfo("i1", "icloud", "Me", "ok", 0) };

        var withResources = CalendarSections.Make(calendars, accounts, EngineSettingsView.Defaults, Text);
        var withoutResources = CalendarSections.Make(calendars, accounts, EngineSettingsView.Defaults, text: null);

        withoutResources.Groups.Select(g => (g.Title, g.Subtitle, g.AccountName)).Should().Equal(withResources.Groups.Select(g => (g.Title, g.Subtitle, g.AccountName)));
        foreach (var kind in new[] { "google", "microsoft", "icloud", "caldav", "icalsub" })
        {
            CalendarSections.ProviderName(kind, null).Should().Be(CalendarSections.ProviderName(kind, Text));
        }
    }

    // ---- through the engine's state ---------------------------------------------------------------------------------

    [Fact]
    public void The_golden_state_has_a_google_section_and_a_hidden_subscribed_calendar_set_apart()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "state.json");
        var payload = JsonNode.Parse(File.ReadAllText(path))!["payload"]!;
        var state = EngineMapping.ToAppState(payload.Deserialize<Pkg.EngineState>(WireOptions)!, Text);

        var group = state.CalendarGroups.Should().ContainSingle().Subject;
        group.Title.Should().Be("Google");
        group.Calendars.Should().ContainSingle().Which.Should().Be(
            new CalendarEntry("google-1/primary", "Work", "#3366CC", IsShown: true, Tugs: true, "standard", false, false, "me@example.com"));
        state.SystemCalendars.Should().ContainSingle().Which.Should().Be(
            new CalendarEntry("google-1/holidays", "Holidays", null, IsShown: false, Tugs: false, "subscribed", true, true));
    }

    [Fact]
    public void A_calendar_that_leaves_the_engines_state_leaves_the_list()
    {
        var withBoth = new Pkg.EngineState
        {
            Accounts = [new Pkg.Account { KindID = "google", ConnectionID = "g1", DisplayName = "me@gmail.com" }],
            Calendars = [Cal("google-g1", "1", "Work", "me@gmail.com"), Cal("google-g1", "2", "Home", "me@gmail.com")],
        };
        var withOne = new Pkg.EngineState { Accounts = withBoth.Accounts, Calendars = [withBoth.Calendars[0]] };

        EngineMapping.ToAppState(withBoth, Text).CalendarGroups.Single().Calendars.Should().HaveCount(2);
        EngineMapping.ToAppState(withOne, Text).CalendarGroups.Single().Calendars.Select(c => c.Title).Should().Equal("Work");
    }
}
