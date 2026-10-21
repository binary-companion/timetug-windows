using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

public class SettingsShellTests
{
    private readonly FakeEngineClient _engine = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeUiSettings _ui = new();
    private readonly SettingsText _text = new(new ReswLocalizer());

    /// <summary>The highlight clears from a continuation of the injected delay, so the test waits a moment for it.</summary>
    private static async Task Eventually(Func<bool> condition)
    {
        for (var waited = 0; waited < 3000 && !condition(); waited += 5)
        {
            await Task.Delay(5);
        }
    }

    private SettingsShellViewModel Shell() => new(_engine, _text, _ui, new ImmediateDispatcher(), _time);

    private static AppState StateWith(params CalendarEntry[] calendars) =>
        new([], [], [], null, null, false, true)
        {
            CalendarGroups = [new CalendarGroup("me", "google-1", "google", CalendarOrigin.Account, "me · connected directly", false, calendars) { Title = "Google" }],
        };

    private static CalendarEntry Calendar(string key, string title) => new(key, title, null, true, false, "standard", false, false, "me");

    // ---- the panes ----------------------------------------------------------------------------------------------

    [Fact]
    public void The_panes_are_the_macs_in_the_macs_order_with_the_macs_titles()
    {
        Enum.GetValues<SettingsPane>().Should().Equal(SettingsPane.General, SettingsPane.Appearance, SettingsPane.Accounts, SettingsPane.Calendars, SettingsPane.TugRules);
        using var shell = Shell();

        shell.Panes.Select(p => p.Pane).Should().Equal(Enum.GetValues<SettingsPane>());
        shell.Panes.Select(p => p.Title).Should().Equal("General", "Appearance", "Accounts", "Calendars", "Tug Rules");
        Enum.GetValues<GeneralDestination>().Should().Equal(GeneralDestination.About, GeneralDestination.SoftwareUpdate);
    }

    [Fact]
    public void It_starts_on_general_with_no_search_and_no_highlight()
    {
        using var shell = Shell();

        shell.SelectedPane.Should().Be(SettingsPane.General);
        shell.GeneralPath.Should().BeEmpty();
        shell.Destination.Should().BeNull();
        shell.SearchText.Should().BeEmpty();
        shell.IsSearching.Should().BeFalse();
        shell.Results.Should().BeEmpty();
        shell.HighlightedId.Should().BeNull();
    }

    [Fact]
    public void Choosing_a_pane_selects_it_and_raises_one_change()
    {
        using var shell = Shell();
        var changes = new List<string?>();
        shell.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        shell.SelectedPane = SettingsPane.Calendars;
        shell.SelectedPane = SettingsPane.Calendars;

        shell.SelectedPane.Should().Be(SettingsPane.Calendars);
        changes.Count(c => c == nameof(SettingsShellViewModel.SelectedPane)).Should().Be(1);
    }

    // ---- opening a pane and highlighting a control -----------------------------------------------------------------

    [Fact]
    public async Task Open_selects_the_pane_and_sets_the_highlight_once_for_about_a_second_and_a_half()
    {
        using var shell = Shell();
        var highlights = new List<string?>();
        shell.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsShellViewModel.HighlightedId))
            {
                highlights.Add(shell.HighlightedId);
            }
        };

        shell.Open(SettingsPane.TugRules, "lead-time");

        shell.SelectedPane.Should().Be(SettingsPane.TugRules);
        shell.HighlightedId.Should().Be("lead-time");
        _time.Advance(TimeSpan.FromMilliseconds(1499));
        await Task.Delay(50);
        shell.HighlightedId.Should().Be("lead-time");
        _time.Advance(TimeSpan.FromMilliseconds(2));
        await Eventually(() => shell.HighlightedId is null);
        shell.HighlightedId.Should().BeNull();
        highlights.Should().Equal("lead-time", null);
        SettingsShellViewModel.HighlightDuration.Should().Be(TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public async Task A_second_open_restarts_the_highlight_and_the_first_one_does_not_clear_it_early()
    {
        using var shell = Shell();

        shell.Open(SettingsPane.TugRules, "lead-time");
        _time.Advance(TimeSpan.FromMilliseconds(1000));
        shell.Open(SettingsPane.Calendars, "skip-all-day");
        _time.Advance(TimeSpan.FromMilliseconds(1000));
        await Task.Delay(50);

        shell.HighlightedId.Should().Be("skip-all-day", "the first highlight's timer was cancelled");
        _time.Advance(TimeSpan.FromMilliseconds(600));
        await Eventually(() => shell.HighlightedId is null);
        shell.HighlightedId.Should().BeNull();
    }

    [Fact]
    public void Open_without_a_pane_stays_on_the_current_pane_and_open_without_a_highlight_sets_none()
    {
        using var shell = Shell();
        shell.SelectedPane = SettingsPane.Accounts;

        shell.Open(null, "diagnostics");
        shell.HighlightedId.Should().Be("diagnostics");
        shell.SelectedPane.Should().Be(SettingsPane.Accounts);

        shell.Open(SettingsPane.Appearance);
        shell.SelectedPane.Should().Be(SettingsPane.Appearance);
        shell.HighlightedId.Should().Be("diagnostics", "opening a pane alone leaves a running highlight to run out");
    }

    [Fact]
    public void Open_announces_each_request_so_the_pane_can_scroll_to_the_control()
    {
        using var shell = Shell();
        var requests = new List<string>();
        shell.HighlightRequested += requests.Add;

        shell.Open(SettingsPane.TugRules, "lead-time");
        shell.Open(SettingsPane.TugRules, "lead-time");
        shell.Open(SettingsPane.Appearance);

        requests.Should().Equal("lead-time", "lead-time");
    }

    [Fact]
    public void Open_can_select_a_general_sub_page_and_the_back_button_leaves_it()
    {
        using var shell = Shell();

        shell.Open(SettingsPane.General, null, GeneralDestination.About);
        shell.GeneralPath.Should().Equal(GeneralDestination.About);
        shell.Destination.Should().Be(GeneralDestination.About);
        shell.CanGoBack.Should().BeTrue();

        shell.Back();
        shell.GeneralPath.Should().BeEmpty();
        shell.CanGoBack.Should().BeFalse();
        shell.Back();
        shell.GeneralPath.Should().BeEmpty("back at the hub does nothing");
    }

    [Fact]
    public void Open_without_a_destination_returns_general_to_its_hub_so_the_highlighted_control_is_visible()
    {
        using var shell = Shell();
        shell.Open(SettingsPane.General, null, GeneralDestination.SoftwareUpdate);

        shell.Open(SettingsPane.General, "launch-at-login");

        shell.GeneralPath.Should().BeEmpty();
        shell.HighlightedId.Should().Be("launch-at-login");
    }

    [Fact]
    public void A_hub_row_opens_its_sub_page()
    {
        using var shell = Shell();

        shell.OpenDestination(GeneralDestination.SoftwareUpdate);

        shell.SelectedPane.Should().Be(SettingsPane.General);
        shell.GeneralPath.Should().Equal(GeneralDestination.SoftwareUpdate);
    }

    [Fact]
    public void The_highlight_fades_unless_windows_asks_for_reduced_motion()
    {
        using var shell = Shell();
        shell.AnimateHighlight.Should().BeTrue();

        _ui.AnimationsEnabled = false;
        shell.AnimateHighlight.Should().BeFalse();
    }

    // ---- searching ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Typing_shows_the_matching_controls_with_the_pane_each_lives_in()
    {
        using var shell = Shell();

        shell.SearchText = "lead";

        shell.IsSearching.Should().BeTrue();
        var result = shell.Results.Should().ContainSingle(r => r.Item.Id == "lead-time").Subject;
        result.Title.Should().Be("Lead time");
        result.PaneTitle.Should().Be("Tug Rules");
        shell.HasNoResults.Should().BeFalse();
    }

    [Fact]
    public void A_search_with_no_match_says_so_and_clearing_it_goes_back_to_the_panes()
    {
        using var shell = Shell();

        shell.SearchText = "zzzz";
        shell.Results.Should().BeEmpty();
        shell.HasNoResults.Should().BeTrue();

        shell.SearchText = "  ";
        shell.IsSearching.Should().BeFalse();
        shell.HasNoResults.Should().BeFalse();
    }

    [Fact]
    public void Search_includes_the_calendars_in_the_engines_state_and_follows_it()
    {
        using var shell = Shell();
        shell.SearchText = "cafe";
        shell.Results.Should().BeEmpty();

        _engine.Push(StateWith(Calendar("google-1/a", "Café Sync")));
        shell.Results.Should().ContainSingle().Which.Item.Id.Should().Be("calendar:google-1/a");

        _engine.Push(StateWith());
        shell.Results.Should().BeEmpty("a calendar that leaves the state leaves the results");
    }

    [Fact]
    public void Search_includes_the_subscribed_and_system_calendars_too()
    {
        using var shell = Shell();
        _engine.Push(StateWith() with { SystemCalendars = [new CalendarEntry("google-1/h", "Holidays", null, false, false, "subscribed", true, true)] });

        shell.SearchText = "holiday";

        shell.Results.Select(r => r.Item.Id).Should().Equal("calendar:google-1/h");
    }

    [Fact]
    public void Choosing_a_result_opens_its_pane_and_highlights_it()
    {
        using var shell = Shell();
        shell.SearchText = "lead";

        shell.Reveal(shell.Results[0].Item);

        shell.SelectedPane.Should().Be(SettingsPane.TugRules);
        shell.HighlightedId.Should().Be("lead-time");
    }

    [Fact]
    public void Choosing_the_about_result_drills_into_about_and_a_calendar_opens_the_calendars_pane()
    {
        using var shell = Shell();
        _engine.Push(StateWith(Calendar("google-1/a", "Work")));

        shell.SearchText = "version";
        shell.Reveal(shell.Results.Single(r => r.Item.Id == "about").Item);
        shell.SelectedPane.Should().Be(SettingsPane.General);
        shell.GeneralPath.Should().Equal(GeneralDestination.About);

        shell.SearchText = "work";
        shell.Reveal(shell.Results.Single(r => r.Item.Id == "calendar:google-1/a").Item);
        shell.SelectedPane.Should().Be(SettingsPane.Calendars);
        shell.GeneralPath.Should().BeEmpty();
        shell.HighlightedId.Should().Be("calendar:google-1/a");
    }

    [Fact]
    public void Dispose_stops_following_the_engine_and_the_highlight_timer()
    {
        var shell = Shell();
        shell.Open(SettingsPane.TugRules, "lead-time");

        shell.Dispose();
        _engine.Push(StateWith(Calendar("google-1/a", "Café")));
        _time.Advance(TimeSpan.FromSeconds(5));

        shell.Results.Should().BeEmpty();
    }
}
