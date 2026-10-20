using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Tray;

namespace TimeTug.Presentation.Tests.Tray;

internal sealed class FakeTrayHost : ITrayHost
{
    public int Welcomes { get; private set; }

    public int TestTugs { get; private set; }

    public int Quits { get; private set; }

    public void ShowWelcome() => Welcomes++;

    public ValueTask TestTugAsync()
    {
        TestTugs++;
        return ValueTask.CompletedTask;
    }

    public ValueTask QuitAsync()
    {
        Quits++;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeAttention : ITugAttention
{
    private bool _needs;

    public bool NeedsAttention
    {
        get => _needs;
        set
        {
            _needs = value;
            AttentionChanged?.Invoke();
        }
    }

    public event Action? AttentionChanged;
}

internal sealed class FakeMinuteTicker : IMinuteTicker
{
    private Action? _tick;

    public int Started { get; private set; }

    public int Stopped { get; private set; }

    public IDisposable Start(Action tick)
    {
        Started++;
        _tick = tick;
        return new Stop(this);
    }

    public void Tick() => _tick?.Invoke();

    private sealed class Stop(FakeMinuteTicker owner) : IDisposable
    {
        public void Dispose()
        {
            owner.Stopped++;
            owner._tick = null;
        }
    }
}

public sealed class TrayViewModelTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeEngineClient _engine = new();
    private readonly FakeAttention _attention = new();
    private readonly FakeUiSettings _ui = new();
    private readonly FakeTrayHost _host = new();
    private readonly FakeMinuteTicker _ticker = new();
    private readonly FakeTimeProvider _time = new();
    private readonly TrayViewModel _model;
    private int _changes;

    public TrayViewModelTests()
    {
        _time.SetUtcNow(Noon);
        _model = new TrayViewModel(_engine, _attention, _ui, new ReswLocalizer(), _host, _time, _ticker, CultureInfo.GetCultureInfo("en-US"));
        _model.Changed += () => _changes++;
    }

    public void Dispose() => _model.Dispose();

    private static IReadOnlyList<AccountInfo> OneAccount => [States.Google()];

    private static AppState State(
        IReadOnlyList<AccountInfo>? accounts = null,
        EventSummary? next = null,
        bool takeoverPending = true,
        bool enabled = true)
    {
        var events = next is null ? [] : new List<EventSummary> { next };
        var nextMeeting = next is null ? null : new NextMeetingInfo(next.Title, next.Start, next.HasJoinLink);
        var nextTakeover = next is not null && takeoverPending ? new NextTakeoverInfo(next.Id, next.Start.AddMinutes(-1)) : null;
        return new AppState(accounts ?? OneAccount, States.Kinds, events, nextMeeting, nextTakeover, false, enabled);
    }

    private static EventSummary Meeting(int minutesFromNoon, bool link = false, string title = "Design review") =>
        new("evt-1", title, Noon.AddMinutes(minutesFromNoon), Noon.AddMinutes(minutesFromNoon + 30), false, link);

    private IEnumerable<string> Texts => _model.Current.Rows.Where(r => r.Kind != TrayRowKind.Separator).Select(r => r.Text);

    private TrayRow Row(TrayCommand command) => _model.Current.Rows.Single(r => r.Command == command);

    // ---- the menu's shape ---------------------------------------------------------------------------------------

    [Fact]
    public void The_menu_has_at_most_six_rows_in_the_grouped_order_of_the_program_design()
    {
        _engine.Push(State(next: Meeting(150)));

        var rows = _model.Current.Rows;

        rows.Where(r => r.Kind != TrayRowKind.Separator).Should().HaveCountLessThanOrEqualTo(6);
        rows.Select(r => r.Command).Should().Equal(
            TrayCommand.Next, null, TrayCommand.EnableTug, TrayCommand.TestTug, null, TrayCommand.Settings, null, TrayCommand.Quit);
        rows.Select(r => r.Kind).Should().Equal(
            TrayRowKind.Information, TrayRowKind.Separator, TrayRowKind.Toggle, TrayRowKind.Command, TrayRowKind.Separator,
            TrayRowKind.Command, TrayRowKind.Separator, TrayRowKind.Command);
        rows[0].Kind.Should().Be(TrayRowKind.Information);
        rows[^1].Command.Should().Be(TrayCommand.Quit);
    }

    [Fact]
    public void Rows_have_the_wording_of_the_program_design()
    {
        _engine.Push(State(next: Meeting(150)));

        Texts.Should().Equal("Next: Design review · 2:30 PM", "Enable Tug", "Test tug", "Settings…", "Quit TimeTug");
    }

    [Fact]
    public void Settings_is_a_disabled_placeholder_until_slice_three()
    {
        _engine.Push(State());

        Row(TrayCommand.Settings).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Before_the_first_engine_state_only_Quit_is_usable()
    {
        Texts.First().Should().Be("Starting…");
        Row(TrayCommand.EnableTug).IsEnabled.Should().BeFalse();
        Row(TrayCommand.TestTug).IsEnabled.Should().BeFalse();
        Row(TrayCommand.Quit).IsEnabled.Should().BeTrue();
    }

    // ---- the Next row -------------------------------------------------------------------------------------------

    [Fact]
    public void Next_is_information_only()
    {
        _engine.Push(State(next: Meeting(150)));

        Row(TrayCommand.Next).Kind.Should().Be(TrayRowKind.Information);
        Row(TrayCommand.Next).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Next_comes_from_the_engines_next_meeting_not_from_the_event_list()
    {
        // The list holds an earlier event, but the engine names the next meeting: the menu shows what the engine says.
        var state = State(next: Meeting(150, title: "Engine's pick")) with
        {
            Events = [new EventSummary("evt-0", "Earlier in the list", Noon.AddMinutes(30), Noon.AddMinutes(60), false, false)],
        };

        _engine.Push(state);

        Texts.First().Should().Be("Next: Engine's pick · 2:30 PM");
    }

    [Fact]
    public void Next_says_so_when_nothing_is_coming_up()
    {
        _engine.Push(State());

        Texts.First().Should().Be("No upcoming meetings");
    }

    [Fact]
    public void Next_time_is_in_the_local_time_zone_of_the_time_provider()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-5), "test", "test"));

        _engine.Push(State(next: Meeting(150)));

        Texts.First().Should().Be("Next: Design review · 9:30 AM");
    }

    [Fact]
    public void A_long_title_is_shortened_so_the_menu_stays_narrow()
    {
        _engine.Push(State(next: Meeting(150, title: new string('x', 200))));

        Texts.First().Length.Should().BeLessThan(90);
        Texts.First().Should().EndWith(" · 2:30 PM");
    }

    [Fact]
    public void Next_becomes_Join_once_the_engine_says_a_link_is_near()
    {
        _engine.Push(State(next: Meeting(8, link: true)));

        Texts.First().Should().Be("Join Design review");
    }

    [Fact]
    public void Next_stays_information_when_the_meeting_is_near_but_has_no_link()
    {
        _engine.Push(State(next: Meeting(8, link: false)));

        Texts.First().Should().Be("Next: Design review · 12:08 PM");
    }

    [Fact]
    public void Next_stays_information_when_there_is_a_link_but_it_is_not_near()
    {
        _engine.Push(State(next: Meeting(90, link: true)));

        Texts.First().Should().Be("Next: Design review · 1:30 PM");
    }

    [Fact]
    public void Next_is_not_Join_when_the_engine_has_no_pending_takeover_for_it()
    {
        // Tug turned off, or already presented: the engine reports no next takeover, so nothing is "near".
        _engine.Push(State(next: Meeting(8, link: true), takeoverPending: false));

        Texts.First().Should().Be("Next: Design review · 12:08 PM");
    }

    [Fact]
    public void Next_is_not_Join_when_the_next_takeover_is_for_a_different_meeting()
    {
        var state = State(next: Meeting(8, link: true)) with { NextTakeover = new NextTakeoverInfo("evt-other", Noon.AddMinutes(60)) };

        _engine.Push(state);

        Texts.First().Should().Be("Next: Design review · 12:08 PM");
    }

    [Fact]
    public void The_Join_wording_arrives_when_the_clock_reaches_the_window()
    {
        _engine.Push(State(next: Meeting(30, link: true)));
        Texts.First().Should().StartWith("Next:");

        _time.Advance(TimeSpan.FromMinutes(20));
        _ticker.Tick();

        Texts.First().Should().Be("Join Design review");
    }

    // ---- Enable Tug ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Enable_Tug_is_checked_when_the_engine_setting_is_on()
    {
        _engine.Push(State(enabled: true));

        Row(TrayCommand.EnableTug).IsChecked.Should().BeTrue();
        Row(TrayCommand.EnableTug).Kind.Should().Be(TrayRowKind.Toggle);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Enable_Tug_round_trips_through_the_engine_setting()
    {
        _engine.Push(State(enabled: true));

        await _model.InvokeAsync(TrayCommand.EnableTug);

        _engine.Sent.Should().Equal("enabled:False");
        Row(TrayCommand.EnableTug).IsChecked.Should().BeTrue("the menu follows the engine's answer, not the click");

        _engine.Push(State(enabled: false));
        Row(TrayCommand.EnableTug).IsChecked.Should().BeFalse();

        await _model.InvokeAsync(TrayCommand.EnableTug);

        _engine.Sent.Should().Equal("enabled:False", "enabled:True");
        _engine.Push(State(enabled: true));
        Row(TrayCommand.EnableTug).IsChecked.Should().BeTrue();
    }

    [Fact]
    public async Task Enable_Tug_does_nothing_before_the_engine_has_spoken()
    {
        await _model.InvokeAsync(TrayCommand.EnableTug);

        _engine.Sent.Should().BeEmpty();
    }

    // ---- Test tug, Settings, Quit -------------------------------------------------------------------------------

    [Fact]
    public async Task Test_tug_asks_the_host_for_the_test_tug()
    {
        _engine.Push(State());

        await _model.InvokeAsync(TrayCommand.TestTug);

        _host.TestTugs.Should().Be(1);
    }

    [Fact]
    public async Task Settings_and_Next_do_nothing_when_invoked()
    {
        _engine.Push(State(next: Meeting(8, link: true)));

        await _model.InvokeAsync(TrayCommand.Settings);
        await _model.InvokeAsync(TrayCommand.Next);

        _engine.Sent.Should().BeEmpty();
        (_host.Quits + _host.TestTugs + _host.Welcomes).Should().Be(0);
    }

    [Fact]
    public async Task Quit_calls_the_application_shutdown_exactly_once()
    {
        _engine.Push(State());

        await _model.InvokeAsync(TrayCommand.Quit);
        await _model.InvokeAsync(TrayCommand.Quit);

        _host.Quits.Should().Be(1);
    }

    [Fact]
    public async Task Quit_works_before_the_engine_has_spoken()
    {
        await _model.InvokeAsync(TrayCommand.Quit);

        _host.Quits.Should().Be(1);
    }

    // ---- the icon -----------------------------------------------------------------------------------------------

    [Fact]
    public void The_icon_is_idle_with_nothing_near()
    {
        _engine.Push(State(next: Meeting(90)));

        _model.Current.Icon.State.Should().Be(TrayIconState.Idle);
    }

    [Fact]
    public void The_icon_is_soon_when_the_engines_next_takeover_is_within_ten_minutes()
    {
        _engine.Push(State(next: Meeting(9)));

        _model.Current.Icon.State.Should().Be(TrayIconState.Soon);
    }

    [Fact]
    public void The_icon_is_not_soon_without_a_pending_takeover()
    {
        _engine.Push(State(next: Meeting(3), takeoverPending: false));

        _model.Current.Icon.State.Should().Be(TrayIconState.Idle);
    }

    [Fact]
    public void The_icon_is_soon_for_a_takeover_whose_time_has_come()
    {
        _engine.Push(State(next: Meeting(-2)));

        _model.Current.Icon.State.Should().Be(TrayIconState.Soon);
    }

    [Fact]
    public void The_icon_turns_soon_as_the_clock_reaches_the_window()
    {
        _engine.Push(State(next: Meeting(25)));
        _model.Current.Icon.State.Should().Be(TrayIconState.Idle);
        var before = _changes;

        _time.Advance(TimeSpan.FromMinutes(15));
        _ticker.Tick();

        _model.Current.Icon.State.Should().Be(TrayIconState.Soon);
        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public void The_icon_follows_the_controllers_attention_flag()
    {
        _engine.Push(State(next: Meeting(90)));

        _attention.NeedsAttention = true;
        _model.Current.Icon.State.Should().Be(TrayIconState.Attention);

        _attention.NeedsAttention = false;
        _model.Current.Icon.State.Should().Be(TrayIconState.Idle);
    }

    [Fact]
    public void Attention_wins_over_soon()
    {
        _engine.Push(State(next: Meeting(5)));
        _attention.NeedsAttention = true;

        _model.Current.Icon.State.Should().Be(TrayIconState.Attention);
    }

    [Theory]
    [InlineData(true, "tray-light.ico")]
    [InlineData(false, "tray-dark.ico")]
    public void The_idle_icon_follows_the_taskbar_theme(bool lightTaskbar, string file)
    {
        _ui.TaskbarUsesLightTheme = lightTaskbar;
        _engine.Push(State());

        _model.Current.Icon.FileName.Should().Be(file);
    }

    [Fact]
    public void The_tray_icon_ignores_the_app_theme_and_follows_only_the_taskbar_theme()
    {
        _engine.Push(State());
        _ui.TaskbarUsesLightTheme = true;
        _ui.HighContrast = true;
        _ui.TransparencyEffectsEnabled = false;
        _ui.Raise();

        _model.Current.Icon.FileName.Should().Be("tray-light.ico");

        _ui.TaskbarUsesLightTheme = false;
        _ui.Raise();

        _model.Current.Icon.FileName.Should().Be("tray-dark.ico");
    }

    [Fact]
    public void A_taskbar_theme_change_raises_Changed()
    {
        _engine.Push(State());
        var before = _changes;

        _ui.TaskbarUsesLightTheme = !_ui.TaskbarUsesLightTheme;
        _ui.Raise();

        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public void Soon_and_attention_use_artwork_that_reads_on_either_taskbar()
    {
        _engine.Push(State(next: Meeting(5)));

        _ui.TaskbarUsesLightTheme = true;
        var light = _model.Current.Icon.FileName;
        _ui.TaskbarUsesLightTheme = false;
        _ui.Raise();

        _model.Current.Icon.FileName.Should().Be("tray-color.ico").And.Be(light);
        _attention.NeedsAttention = true;
        _model.Current.Icon.FileName.Should().Be("tray-attention.ico");
    }

    // ---- the tooltip --------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(4, "Design review in 4 min")]
    [InlineData(1, "Design review in 1 min")]
    [InlineData(0, "Design review starts now")]
    [InlineData(90, "Design review in 1 hr 30 min")]
    [InlineData(120, "Design review in 2 hr")]
    public void The_tooltip_says_how_long_until_the_next_meeting(int minutes, string expected)
    {
        _engine.Push(State(next: Meeting(minutes)));

        _model.Current.Tooltip.Should().Be(expected);
    }

    [Fact]
    public void The_tooltip_counts_down_as_the_minute_ticker_fires()
    {
        _engine.Push(State(next: Meeting(4)));
        var before = _changes;

        _time.Advance(TimeSpan.FromMinutes(1));
        _ticker.Tick();

        _model.Current.Tooltip.Should().Be("Design review in 3 min");
        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public void A_tick_that_changes_nothing_raises_nothing()
    {
        _engine.Push(State());
        var before = _changes;

        _ticker.Tick();

        _changes.Should().Be(before);
    }

    [Fact]
    public void The_tooltip_says_no_more_meetings_when_the_day_has_none_left()
    {
        _engine.Push(State());

        _model.Current.Tooltip.Should().Be("No more meetings today");
    }

    [Fact]
    public void The_tooltip_names_the_app_before_the_engine_has_spoken_and_with_no_account()
    {
        _model.Current.Tooltip.Should().Be("TimeTug");

        _engine.Push(State(accounts: []));

        _model.Current.Tooltip.Should().Be("TimeTug");
    }

    [Fact]
    public void The_tooltip_says_when_the_tug_is_off_even_with_no_meeting_left()
    {
        _engine.Push(State(enabled: false));

        _model.Current.Tooltip.Should().Be("No more meetings today · Tug is off");
    }

    [Fact]
    public void The_tooltip_says_when_the_tug_is_off()
    {
        _engine.Push(State(next: Meeting(4), enabled: false));

        _model.Current.Tooltip.Should().Be("Design review in 4 min · Tug is off");
    }

    [Fact]
    public void The_tooltip_says_a_meeting_needs_attention()
    {
        _engine.Push(State(next: Meeting(4)));
        _attention.NeedsAttention = true;

        _model.Current.Tooltip.Should().Be("TimeTug: a meeting needs you");
    }

    [Fact]
    public void The_tooltip_fits_the_127_characters_windows_allows()
    {
        _engine.Push(State(next: Meeting(4, title: new string('y', 400))));

        _model.Current.Tooltip.Length.Should().BeLessThanOrEqualTo(127);
        _model.Current.Tooltip.Should().EndWith(" in 4 min");
    }

    [Theory]
    [InlineData(270, "Design review in 4 min")]
    [InlineData(59, "Design review in 1 min")]
    [InlineData(1, "Design review in 1 min")]
    [InlineData(5400 + 59, "Design review in 1 hr 30 min")]
    public void The_tooltip_counts_whole_minutes_down_like_the_digit_does(int seconds, string expected)
    {
        _engine.Push(State(next: Meeting(0) with { Start = Noon.AddSeconds(seconds), End = Noon.AddSeconds(seconds + 1800) }));

        _model.Current.Tooltip.Should().Be(expected);
    }

    // ---- the minute digit ---------------------------------------------------------------------------------------
    // The rules follow the Mac's menu-bar code: the icon turns "soon" when the next pending takeover starts within ten minutes
    // (MenuBarIconState.soonWindow, inclusive) and the countdown is TimeFormatting.compact, which truncates to whole minutes and
    // switches to seconds below one minute.

    private static EventSummary Starting(int seconds, bool allDay = false, string id = "evt-1") =>
        new(id, "Design review", Noon.AddSeconds(seconds), Noon.AddSeconds(seconds + 1800), allDay, false);

    [Theory]
    [InlineData(1800, null)]
    [InlineData(601, null)]
    [InlineData(600, null)]
    [InlineData(599, 9)]
    [InlineData(541, 9)]
    [InlineData(540, 9)]
    [InlineData(539, 8)]
    [InlineData(300, 5)]
    [InlineData(299, 4)]
    [InlineData(61, 1)]
    [InlineData(60, 1)]
    [InlineData(59, null)]
    [InlineData(30, null)]
    [InlineData(1, null)]
    [InlineData(0, null)]
    [InlineData(-30, null)]
    [InlineData(-900, null)]
    public void The_digit_is_the_remaining_whole_minutes_in_the_last_ten(int remainingSeconds, int? digit)
    {
        TrayIconPlan.DigitFor(TimeSpan.FromSeconds(remainingSeconds)).Should().Be(digit);
        _engine.Push(State(next: Starting(remainingSeconds)));

        _model.Current.Icon.Digit.Should().Be(digit);
        _model.Current.Plan.Digit.Should().Be(digit);
    }

    [Fact]
    public void A_digit_goes_with_the_soon_icon_and_the_plan_says_so()
    {
        _engine.Push(State(next: Starting(300)));

        _model.Current.Plan.Should().Be(new TrayIconPlan(TrayIconState.Soon, 5, "Design review in 5 min"));
    }

    [Fact]
    public void Outside_the_window_the_plan_is_idle_with_no_digit()
    {
        _engine.Push(State(next: Starting(3600)));

        _model.Current.Plan.Should().Be(new TrayIconPlan(TrayIconState.Idle, null, "Design review in 1 hr"));
    }

    [Fact]
    public void There_is_no_digit_with_no_next_meeting()
    {
        _engine.Push(State());

        _model.Current.Plan.Should().Be(new TrayIconPlan(TrayIconState.Idle, null, "No more meetings today"));
    }

    [Fact]
    public void There_is_no_digit_when_the_engine_has_no_takeover_pending_for_the_meeting()
    {
        _engine.Push(State(next: Starting(300), takeoverPending: false));

        _model.Current.Icon.Should().Match<TrayIconView>(i => i.State == TrayIconState.Idle && i.Digit == null);
    }

    [Fact]
    public void Attention_overrides_the_digit()
    {
        _engine.Push(State(next: Starting(300)));
        _model.Current.Icon.Digit.Should().Be(5);

        _attention.NeedsAttention = true;

        _model.Current.Plan.Should().Be(new TrayIconPlan(TrayIconState.Attention, null, "TimeTug: a meeting needs you"));

        _attention.NeedsAttention = false;
        _model.Current.Icon.Digit.Should().Be(5);
    }

    [Fact]
    public void An_all_day_event_never_produces_a_digit()
    {
        // The next all-day event starts at midnight; with a takeover pending for it, five minutes before midnight is "soon" for the icon
        // but there is no countdown to count.
        _engine.Push(State(next: Starting(300, allDay: true)));

        _model.Current.Icon.Digit.Should().BeNull();
    }

    [Fact]
    public void The_digit_comes_from_the_start_of_the_meeting_not_from_the_moment_the_takeover_fires()
    {
        // The engine fires the tug a minute (the lead time) before the start; the countdown is to the meeting.
        _engine.Push(State(next: Starting(300)));

        _model.Current.Icon.Digit.Should().Be(5, "not 4, which is the time left until the takeover fires");
    }

    [Fact]
    public void The_digit_counts_down_on_the_minute_ticker_and_raises_Changed()
    {
        _engine.Push(State(next: Starting(599)));
        _model.Current.Icon.Digit.Should().Be(9);
        var before = _changes;

        _time.Advance(TimeSpan.FromMinutes(1));
        _ticker.Tick();

        _model.Current.Icon.Digit.Should().Be(8);
        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public void The_digit_appears_when_the_clock_enters_the_window_and_goes_when_the_meeting_starts()
    {
        _engine.Push(State(next: Starting(700)));
        _model.Current.Icon.Digit.Should().BeNull();

        _time.Advance(TimeSpan.FromSeconds(101));
        _ticker.Tick();
        _model.Current.Icon.Digit.Should().Be(9);

        _time.Advance(TimeSpan.FromSeconds(560));
        _ticker.Tick();
        _model.Current.Icon.Digit.Should().BeNull("under a minute left");

        _time.Advance(TimeSpan.FromSeconds(60));
        _ticker.Tick();
        _model.Current.Icon.Digit.Should().BeNull("started");
    }

    [Fact]
    public void A_tick_that_leaves_the_digit_alone_raises_nothing()
    {
        _engine.Push(State(next: Starting(599)));
        var before = _changes;

        _time.Advance(TimeSpan.FromSeconds(30));
        _ticker.Tick();

        _changes.Should().Be(before);
    }

    [Fact]
    public void The_digit_follows_a_midnight_crossing()
    {
        // 23:55:30 on the 16th, a meeting at 00:05 on the 17th.
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 16, 23, 55, 30, TimeSpan.Zero));
        var midnightMeeting = new EventSummary("evt-1", "Early call", new DateTimeOffset(2026, 10, 17, 0, 5, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 17, 0, 35, 0, TimeSpan.Zero), false, false);
        _engine.Push(State(next: midnightMeeting));
        _model.Current.Icon.Digit.Should().Be(9);

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 17, 0, 0, 30, TimeSpan.Zero));
        _ticker.Tick();

        _model.Current.Icon.Digit.Should().Be(4);
    }

    [Fact]
    public void A_clock_change_is_recomputed_on_the_next_tick_and_the_engines_new_state_wins_at_once()
    {
        _engine.Push(State(next: Starting(540)));
        _model.Current.Icon.Digit.Should().Be(9);

        // The clock jumps forward past the start: the next tick drops the digit.
        _time.Advance(TimeSpan.FromMinutes(20));
        _ticker.Tick();
        _model.Current.Icon.Digit.Should().BeNull();

        // The engine, told about the change, names the next meeting; its state is used immediately.
        _engine.Push(State(next: Meeting(23, title: "Next one")));
        _model.Current.Icon.Digit.Should().Be(3);
        _model.Current.Tooltip.Should().Be("Next one in 3 min");
    }

    [Fact]
    public void A_time_zone_change_does_not_move_the_digit_because_it_counts_instants()
    {
        _engine.Push(State(next: Starting(300)));

        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(7), "test", "test"));
        _ticker.Tick();

        _model.Current.Icon.Digit.Should().Be(5);
    }

    [Fact]
    public void Disposing_stops_the_digit_updating()
    {
        _engine.Push(State(next: Starting(599)));
        _model.Dispose();
        var before = _changes;

        _time.Advance(TimeSpan.FromMinutes(1));
        _ticker.Tick();

        _changes.Should().Be(before);
    }

    // ---- left click ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_left_click_opens_the_welcome_window_when_there_are_no_accounts()
    {
        _engine.Push(State(accounts: []));

        _model.PrimaryClick();

        _host.Welcomes.Should().Be(1);
    }

    [Fact]
    public void A_left_click_does_nothing_yet_when_there_are_accounts()
    {
        _engine.Push(State());

        _model.PrimaryClick();

        _host.Welcomes.Should().Be(0);
    }

    [Fact]
    public void A_left_click_before_the_first_state_does_nothing()
    {
        _model.PrimaryClick();

        _host.Welcomes.Should().Be(0);
    }

    // ---- life cycle ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_new_state_raises_Changed_only_when_the_menu_differs()
    {
        _engine.Push(State(next: Meeting(150)));
        var before = _changes;

        _engine.Push(State(next: Meeting(150)));

        _changes.Should().Be(before);

        _engine.Push(State(next: Meeting(150), enabled: false));

        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public void Disposing_stops_the_ticker_and_listening()
    {
        _model.Dispose();
        var before = _changes;

        _engine.Push(State(next: Meeting(4)));
        _attention.NeedsAttention = true;

        _ticker.Stopped.Should().Be(1);
        _changes.Should().Be(before);
    }

    [Fact]
    public void The_view_model_starts_one_ticker()
    {
        _ticker.Started.Should().Be(1);
    }
}
