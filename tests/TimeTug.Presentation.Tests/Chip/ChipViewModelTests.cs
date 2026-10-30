using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Tray;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Chip;

public sealed class ChipViewModelTests : IDisposable
{
    /// <summary>Friday 2026-10-16, noon UTC.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeEngineClient _engine = new();
    private readonly FakeTimeProvider _time = new(Noon);
    private readonly FakeMinuteTicker _ticker = new();
    private readonly MemoryPresentationSettingsStore _store = new();
    private readonly RecordingTugDiagnostics _log = new();
    private readonly List<string> _announcements = [];
    private readonly ChipViewModel _model;
    private int _changes;

    public ChipViewModelTests()
    {
        _model = new ChipViewModel(_engine, _store, _time, _ticker, new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"), _log);
        _model.Changed += () =>
        {
            _changes++;
            if (_model.Plan.Announcement is { } said)
            {
                _announcements.Add(said);
            }
        };
    }

    public void Dispose() => _model.Dispose();

    private static AgendaItem Meeting(
        string id,
        string title,
        TimeSpan start,
        TimeSpan length,
        AgendaPhase phase = AgendaPhase.Upcoming,
        string? color = "#3366CC",
        string? join = null,
        bool next = true) =>
        new(id, title, Noon + start, Noon + start + length, false, phase, "Work", color, join is null ? null : new Uri(join), 0, false, null)
        {
            IsNext = next,
        };

    private static AgendaItem AllDay(string id, string title) =>
        new(id, title, new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero), true,
            AgendaPhase.Upcoming, "Personal", null, null, 0, false, null)
        {
            StartDate = new DateOnly(2026, 10, 16),
            EndDate = new DateOnly(2026, 10, 17),
        };

    private static AppState State(params AgendaItem[] agenda) => States.Empty() with { Agenda = agenda };

    private void Use(ChipMode mode, int lead = 15) =>
        _store.Save(_store.Load() with { ChipMode = mode, ChipLeadMinutes = lead });

    private void At(DateTimeOffset instant)
    {
        _time.SetUtcNow(instant);
        _ticker.Tick();
    }

    // ---- Off, Before meetings, Always ---------------------------------------------------------------------------

    [Fact]
    public void Off_never_shows_whatever_the_day_holds()
    {
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30))));
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(-5), TimeSpan.FromMinutes(30), AgendaPhase.Current)));

        _model.Plan.Visible.Should().BeFalse();
        _model.Plan.Should().Be(ChipPlan.Hidden);
    }

    [Fact]
    public void Nothing_shows_before_the_engines_first_state()
    {
        Use(ChipMode.Always);

        _model.Plan.Visible.Should().BeFalse();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(60)]
    public void Before_meetings_shows_from_the_chosen_minutes_before_the_start_and_not_earlier(int lead)
    {
        Use(ChipMode.BeforeMeetings, lead);
        var start = Noon.AddHours(3);
        _engine.Push(State(Meeting("a", "Planning", start - Noon, TimeSpan.FromMinutes(30))));

        At(start - TimeSpan.FromMinutes(lead) - TimeSpan.FromSeconds(1));
        _model.Plan.Visible.Should().BeFalse("one second before the window opens");

        At(start - TimeSpan.FromMinutes(lead));
        _model.Plan.Visible.Should().BeTrue("exactly N minutes before");

        At(start - TimeSpan.FromMinutes(lead) + TimeSpan.FromSeconds(1));
        _model.Plan.Visible.Should().BeTrue("one second inside the window");
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(60)]
    public void Before_meetings_keeps_showing_while_it_runs_and_stops_at_its_end(int lead)
    {
        Use(ChipMode.BeforeMeetings, lead);
        var start = Noon.AddMinutes(1);
        var end = start.AddMinutes(30);
        _engine.Push(State(Meeting("a", "Planning", start - Noon, TimeSpan.FromMinutes(30))));

        At(start + TimeSpan.FromMinutes(10));
        _model.Plan.Visible.Should().BeTrue("while it runs, even before the engine republishes its phase");

        At(end - TimeSpan.FromSeconds(1));
        _model.Plan.Visible.Should().BeTrue("one second before it ends");

        At(end);
        _model.Plan.Visible.Should().BeFalse("at its end, even if the engine has not yet said it is past");

        At(end + TimeSpan.FromSeconds(1));
        _model.Plan.Visible.Should().BeFalse();
    }

    [Fact]
    public void A_meeting_the_engine_calls_past_is_not_shown()
    {
        Use(ChipMode.Always);
        _engine.Push(State(Meeting("a", "Standup", TimeSpan.FromMinutes(-60), TimeSpan.FromMinutes(30), AgendaPhase.Past, next: false)));

        _model.Plan.Visible.Should().BeFalse();
    }

    [Fact]
    public void Always_shows_the_next_meeting_however_far_off_and_nothing_when_the_day_is_empty()
    {
        Use(ChipMode.Always);
        _engine.Push(State());
        _model.Plan.Visible.Should().BeFalse("no meeting today: hidden, never empty");

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromHours(5), TimeSpan.FromMinutes(30))));
        _model.Plan.Visible.Should().BeTrue();
        _model.Plan.Countdown.Should().Be("in 5 h");

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(-10), TimeSpan.FromMinutes(30), AgendaPhase.Current)));
        _model.Plan.Visible.Should().BeTrue("a meeting in progress counts");
    }

    [Fact]
    public void All_day_events_never_drive_the_chip()
    {
        Use(ChipMode.Always);
        _engine.Push(State(AllDay("h", "Holiday")));
        _model.Plan.Visible.Should().BeFalse();

        _engine.Push(State(AllDay("h", "Holiday"), Meeting("a", "Planning", TimeSpan.FromHours(2), TimeSpan.FromMinutes(30))));
        _model.Plan.Title.Should().Be("Planning");
    }

    [Fact]
    public void A_running_meeting_comes_before_the_next_one()
    {
        Use(ChipMode.Always);
        _engine.Push(State(
            Meeting("now", "Design review", TimeSpan.FromMinutes(-20), TimeSpan.FromMinutes(40), AgendaPhase.Current, next: false),
            Meeting("next", "Planning", TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30))));

        _model.Plan.EventId.Should().Be("now");
    }

    // ---- the words ----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(12 * 60, "in 12 min")]
    [InlineData(11 * 60 + 30, "in 12 min")]
    [InlineData(20, "in 1 min")]
    [InlineData(60, "in 1 min")]
    [InlineData(61, "in 2 min")]
    [InlineData(60 * 60, "in 1 h")]
    [InlineData(65 * 60, "in 1 h 5 min")]
    [InlineData(65 * 60 - 30, "in 1 h 5 min")]
    public void An_upcoming_meeting_counts_down_in_minutes_and_hours_rounded_up(int seconds, string countdown)
    {
        Use(ChipMode.Always);

        _engine.Push(State(Meeting("a", "Planning review", TimeSpan.FromSeconds(seconds), TimeSpan.FromMinutes(30))));

        _model.Plan.Countdown.Should().Be(countdown);
        _model.Plan.Title.Should().Be("Planning review");
        _model.Plan.Text.Should().Be("Planning review · " + countdown);
    }

    [Fact]
    public void A_running_meeting_says_now_and_when_it_ends()
    {
        Use(ChipMode.Always);

        _engine.Push(State(Meeting("a", "Planning review", TimeSpan.FromMinutes(-20), TimeSpan.FromMinutes(50), AgendaPhase.Current)));

        _model.Plan.Countdown.Should().Be("now · ends 12:30 PM");
        _model.Plan.Text.Should().Be("Planning review · now · ends 12:30 PM");
    }

    [Fact]
    public void A_screen_reader_gets_the_whole_sentence()
    {
        Use(ChipMode.Always);

        _engine.Push(State(Meeting("a", "Planning review", TimeSpan.FromMinutes(12), TimeSpan.FromMinutes(30))));
        _model.Plan.AutomationName.Should().Be("Planning review, in 12 minutes");

        _engine.Push(State(Meeting("a", "Planning review", TimeSpan.FromMinutes(-20), TimeSpan.FromMinutes(50), AgendaPhase.Current)));
        _model.Plan.AutomationName.Should().Be("Planning review, in progress, ends 12:30 PM");
    }

    [Fact]
    public void The_accent_is_the_calendars_colour_and_may_be_missing()
    {
        Use(ChipMode.Always);

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), color: "#00FF00")));
        _model.Plan.AccentColorHex.Should().Be("#00FF00");

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), color: null)));
        _model.Plan.AccentColorHex.Should().BeNull();
    }

    [Fact]
    public void Join_is_offered_only_for_a_meeting_with_a_link()
    {
        Use(ChipMode.Always);

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))));
        _model.Plan.CanJoin.Should().BeFalse();

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), join: "https://meet.example.test/abc")));
        _model.Plan.CanJoin.Should().BeTrue();
    }

    // ---- hide until the next meeting ----------------------------------------------------------------------------

    [Fact]
    public void Hide_until_the_next_meeting_hides_it_for_that_meeting_and_for_that_meeting_only()
    {
        Use(ChipMode.Always);
        var first = Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30));
        var second = Meeting("b", "Review", TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(30));
        _engine.Push(State(first, second with { IsNext = false }));

        _model.HideUntilNextMeeting();

        _model.Plan.Visible.Should().BeFalse();
        At(Noon.AddMinutes(10));
        _model.Plan.Visible.Should().BeFalse("it stays hidden while that meeting runs");

        At(Noon.AddMinutes(36));
        _engine.Push(State(first with { Phase = AgendaPhase.Past, IsNext = false }, second));
        _model.Plan.Visible.Should().BeTrue("a different meeting became the one it shows");
        _model.Plan.EventId.Should().Be("b");
    }

    [Fact]
    public void Hide_until_the_next_meeting_does_nothing_while_the_chip_is_hidden()
    {
        Use(ChipMode.Always);
        _engine.Push(State());
        _changes = 0;

        _model.HideUntilNextMeeting();
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))));

        _model.Plan.Visible.Should().BeTrue("nothing was hidden, so the next meeting shows");
    }

    // ---- announcements ------------------------------------------------------------------------------------------

    [Fact]
    public void A_screen_reader_is_told_at_30_15_5_and_1_minute_and_at_the_start_once_each()
    {
        Use(ChipMode.Always);
        var start = Noon.AddHours(1);
        _engine.Push(State(Meeting("a", "Planning", start - Noon, TimeSpan.FromMinutes(30))));

        for (var minute = -40; minute <= 2; minute++)
        {
            At(start.AddMinutes(minute));
        }

        _announcements.Should().Equal(
            "Planning starts in 30 minutes",
            "Planning starts in 15 minutes",
            "Planning starts in 5 minutes",
            "Planning starts in 1 minute",
            "Planning is starting now");
    }

    [Fact]
    public void The_same_minute_seen_again_is_not_announced_again()
    {
        Use(ChipMode.Always);
        var start = Noon.AddHours(1);
        _engine.Push(State(Meeting("a", "Planning", start - Noon, TimeSpan.FromMinutes(30))));

        At(start.AddMinutes(-15));
        At(start.AddMinutes(-15));
        _engine.Push(State(Meeting("a", "Planning", start - Noon, TimeSpan.FromMinutes(30))));

        _announcements.Should().ContainSingle().Which.Should().Be("Planning starts in 15 minutes");
    }

    [Fact]
    public void A_clock_set_back_never_repeats_an_announcement()
    {
        var stepping = new SteppingTime(Noon);
        using var model = new ChipViewModel(_engine, _store, stepping, _ticker, new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"));
        var said = new List<string>();
        model.Changed += () =>
        {
            if (model.Plan.Announcement is { } text)
            {
                said.Add(text);
            }
        };
        Use(ChipMode.Always);
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(30))));

        stepping.Advance(TimeSpan.FromMinutes(10));
        _ticker.Tick();
        said.Should().Equal("Planning starts in 30 minutes");

        stepping.StepWallClock(TimeSpan.FromMinutes(-25));
        _ticker.Tick();
        model.Plan.Countdown.Should().Be("in 55 min", "the plan follows the clock");

        stepping.Advance(TimeSpan.FromMinutes(25));
        _ticker.Tick();
        model.Plan.Countdown.Should().Be("in 30 min");
        said.Should().Equal("Planning starts in 30 minutes");

        stepping.Advance(TimeSpan.FromMinutes(15));
        _ticker.Tick();
        said.Should().Equal("Planning starts in 30 minutes", "Planning starts in 15 minutes");
    }

    [Fact]
    public void A_meeting_first_seen_inside_a_band_is_announced_with_its_real_minutes_and_wider_bands_stay_silent()
    {
        Use(ChipMode.Always);

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30))));
        At(Noon.AddMinutes(5));
        At(Noon.AddMinutes(10));

        _announcements.Should().Equal("Planning starts in 10 minutes", "Planning starts in 5 minutes", "Planning is starting now");
    }

    [Fact]
    public void Nothing_is_announced_while_the_chip_is_hidden()
    {
        Use(ChipMode.BeforeMeetings, 5);
        var start = Noon.AddHours(1);
        _engine.Push(State(Meeting("a", "Planning", start - Noon, TimeSpan.FromMinutes(30))));

        At(start.AddMinutes(-30));
        At(start.AddMinutes(-15));
        _announcements.Should().BeEmpty("the chip is not on screen yet");

        At(start.AddMinutes(-5));
        _announcements.Should().Equal("Planning starts in 5 minutes");
    }

    [Fact]
    public void A_meeting_that_began_long_ago_is_not_announced_when_the_app_opens()
    {
        Use(ChipMode.Always);

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(-20), TimeSpan.FromMinutes(50), AgendaPhase.Current)));

        _model.Plan.Visible.Should().BeTrue();
        _announcements.Should().BeEmpty();
    }

    // ---- change notifications -----------------------------------------------------------------------------------

    [Fact]
    public void Changing_the_mode_raises_changed_once_and_an_unrelated_setting_raises_it_not_at_all()
    {
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))));
        _changes = 0;

        Use(ChipMode.Always);
        _changes.Should().Be(1);
        _model.Plan.Visible.Should().BeTrue();

        _store.Save(_store.Load() with { Theme = AppTheme.Dark });
        _changes.Should().Be(1, "the theme is not the chip's business");

        Use(ChipMode.Off);
        _changes.Should().Be(2);
        _model.Plan.Visible.Should().BeFalse();
    }

    [Fact]
    public void Changing_how_many_minutes_ahead_raises_changed_once_when_it_changes_what_shows()
    {
        Use(ChipMode.BeforeMeetings, 5);
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(20), TimeSpan.FromMinutes(30))));
        _model.Plan.Visible.Should().BeFalse();
        _changes = 0;

        _store.Save(_store.Load() with { ChipLeadMinutes = 30 });

        _changes.Should().Be(1);
        _model.Plan.Visible.Should().BeTrue();
    }

    [Fact]
    public void A_tick_or_a_state_that_changes_nothing_on_the_chip_raises_nothing()
    {
        Use(ChipMode.Always);
        var running = Meeting("a", "Planning", TimeSpan.FromMinutes(-10), TimeSpan.FromMinutes(50), AgendaPhase.Current);
        _engine.Push(State(running));
        _changes = 0;

        _ticker.Tick();
        _engine.Push(State(running));
        _engine.Push(States.Empty() with { Agenda = [running], LeadTime = TimeSpan.FromMinutes(3) });

        _changes.Should().Be(0);
    }

    [Fact]
    public void Each_new_minute_of_the_countdown_raises_changed_once()
    {
        Use(ChipMode.Always);
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(40), TimeSpan.FromMinutes(30))));
        _changes = 0;

        At(Noon.AddMinutes(1));

        _changes.Should().Be(1);
        _model.Plan.Countdown.Should().Be("in 39 min");
    }

    [Fact]
    public void A_zone_change_is_picked_up_on_the_next_tick()
    {
        Use(ChipMode.Always);
        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(-10), TimeSpan.FromMinutes(40), AgendaPhase.Current)));
        _model.Plan.Countdown.Should().Be("now · ends 12:30 PM");
        _changes = 0;

        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-7), "test", "test"));
        _ticker.Tick();

        _model.Plan.Countdown.Should().Be("now · ends 5:30 AM");
        _changes.Should().Be(1);
    }

    [Fact]
    public void After_dispose_it_listens_to_nothing()
    {
        Use(ChipMode.Always);
        _model.Dispose();
        _changes = 0;

        _engine.Push(State(Meeting("a", "Planning", TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))));
        Use(ChipMode.Off);
        _ticker.Tick();

        _changes.Should().Be(0);
        _ticker.Stopped.Should().Be(1);
    }

    // ---- privacy ------------------------------------------------------------------------------------------------

    [Fact]
    public void No_title_link_or_calendar_name_reaches_the_log_through_a_whole_day()
    {
        Use(ChipMode.BeforeMeetings, 15);
        var secrets = new[] { "Quarterly layoffs review", "https://meet.example.test/private-room", "Confidential Calendar" };
        AgendaItem Secret(AgendaPhase phase) =>
            new("s", secrets[0], Noon.AddHours(4), Noon.AddHours(4).AddMinutes(30), false, phase, secrets[2], "#AA0000", new Uri(secrets[1]), 3, false, null) { IsNext = true };

        _engine.Push(State(Secret(AgendaPhase.Upcoming)));
        for (var minute = 0; minute <= 4 * 60 + 40; minute += 5)
        {
            At(Noon.AddMinutes(minute));
            _engine.Push(State(Secret(minute >= 240 ? AgendaPhase.Current : AgendaPhase.Upcoming)));
        }

        _model.HideUntilNextMeeting();
        Use(ChipMode.Off);

        _log.Entries.Should().NotBeEmpty("the chip records when it appears and goes");
        foreach (var secret in secrets)
        {
            _log.Entries.Should().NotContain(e => e.Message.Contains(secret, StringComparison.OrdinalIgnoreCase) || e.Category.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }

        _log.Entries.Should().NotContain(e => e.Message.Contains("example.test", StringComparison.OrdinalIgnoreCase));
    }
}
