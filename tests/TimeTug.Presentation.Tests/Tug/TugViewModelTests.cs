using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Tests.Tug;

internal sealed class FakeUiSettings : IUiSettings
{
    public bool AnimationsEnabled { get; set; } = true;

    public bool TransparencyEffectsEnabled { get; set; } = true;

    public bool HighContrast { get; set; }

    public bool TaskbarUsesLightTheme { get; set; }

    public bool AppsUseDarkTheme { get; set; }

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();
}

internal sealed class RecordingResponder : ITugResponder
{
    public List<string> Calls { get; } = [];

    public ValueTask JoinAsync() => Record("join");

    public ValueTask SnoozeAsync(TimeSpan duration) => Record("snooze:" + (int)duration.TotalSeconds);

    public ValueTask DismissAsync() => Record("dismiss");

    public ValueTask ReportFocusAsync(bool hasKeyboardFocus) => Record("focus:" + hasKeyboardFocus);

    private ValueTask Record(string call)
    {
        Calls.Add(call);
        return ValueTask.CompletedTask;
    }
}

public class TugViewModelTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<TimeSpan> Standard = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600)];

    private sealed class Rig
    {
        public Rig(
            TimeSpan? startsIn = null,
            string? joinUrl = "https://meet.example.test/abc",
            IReadOnlyList<TimeSpan>? snooze = null,
            string? calendar = "Work",
            int others = 3,
            TugMonitorRole role = TugMonitorRole.Primary,
            TimeSpan? length = null,
            bool plusFiveHours = false)
        {
            // Times are formatted for the current culture; pin it so the assertions read the same on every machine.
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Time.SetUtcNow(Noon);
            if (plusFiveHours)
            {
                Time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test-minus-5", TimeSpan.FromHours(-5), "test", "test"));
            }

            var start = Noon + (startsIn ?? TimeSpan.FromSeconds(245));
            Request = new TakeoverRequest("e1", "Design review", start, start + (length ?? TimeSpan.FromMinutes(30)), false, false, joinUrl, snooze ?? Standard, calendar, others);
            Model = new TugViewModel(PlanFor(Request), role, Settings, new ReswLocalizer(), Time, Responder);
        }

        public FakeTimeProvider Time { get; } = new();

        public FakeUiSettings Settings { get; } = new();

        public RecordingResponder Responder { get; } = new();

        public TakeoverRequest Request { get; }

        public TugViewModel Model { get; }

        public static TugPlan PlanFor(TakeoverRequest request, bool clickToRespond = false, string? joinFailed = null) => new(
            TugMode.FullScreen,
            request,
            [new TugMonitorPlan(new MonitorInfo("m1", 0, 0, 1920, 1080, true), TugMonitorRole.Primary)],
            clickToRespond,
            joinFailed,
            null);
    }

    // ---- the countdown, before and after the start --------------------------------------------------------------

    [Fact]
    public void Before_the_start_it_says_starts_in_with_the_minutes_and_seconds_left()
    {
        var rig = new Rig(TimeSpan.FromSeconds(245));

        rig.Model.CountdownLabel.Should().Be("Starts in");
        rig.Model.CountdownValue.Should().Be("4:05");
        rig.Model.IsCountdown.Should().BeTrue();
        rig.Model.Tone.Should().Be(TugTone.Calm);
    }

    [Theory]
    [InlineData(61, TugTone.Calm)]
    [InlineData(60, TugTone.Urgent)]
    [InlineData(5, TugTone.Urgent)]
    public void The_last_minute_is_urgent(int secondsLeft, TugTone tone)
    {
        new Rig(TimeSpan.FromSeconds(secondsLeft)).Model.Tone.Should().Be(tone);
    }

    [Fact]
    public void From_an_hour_away_the_clock_shows_hours()
    {
        new Rig(TimeSpan.FromSeconds(3905)).Model.CountdownValue.Should().Be("1:05:05");
    }

    [Fact]
    public void A_fraction_of_a_second_left_still_reads_one_second_not_zero()
    {
        new Rig(TimeSpan.FromMilliseconds(400)).Model.CountdownValue.Should().Be("0:01");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(-59)]
    public void At_the_start_and_for_the_first_minute_it_says_starting_now(int secondsLeft)
    {
        var rig = new Rig(TimeSpan.FromSeconds(secondsLeft));

        rig.Model.CountdownLabel.Should().BeNull();
        rig.Model.CountdownValue.Should().Be("Starting now");
        rig.Model.IsCountdown.Should().BeFalse();
        rig.Model.Tone.Should().Be(TugTone.Urgent);
    }

    [Theory]
    [InlineData(-60, "Started 1 min ago")]
    [InlineData(-190, "Started 3 min ago")]
    [InlineData(-3599, "Started 59 min ago")]
    [InlineData(-3900, "Started 1h 5m ago")]
    public void After_the_start_it_says_how_long_ago_in_the_late_tone(int secondsLeft, string expected)
    {
        var rig = new Rig(TimeSpan.FromSeconds(secondsLeft));

        rig.Model.CountdownValue.Should().Be(expected);
        rig.Model.CountdownLabel.Should().BeNull();
        rig.Model.Tone.Should().Be(TugTone.Late);
    }

    [Fact]
    public void Refresh_follows_the_time_provider_and_raises_property_changes()
    {
        var rig = new Rig(TimeSpan.FromSeconds(125));
        var changed = new List<string?>();
        rig.Model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Time.Advance(TimeSpan.FromSeconds(65));
        rig.Model.Refresh();

        rig.Model.CountdownValue.Should().Be("1:00");
        rig.Model.Tone.Should().Be(TugTone.Urgent);
        changed.Should().Contain([nameof(TugViewModel.CountdownValue), nameof(TugViewModel.Tone)]);

        rig.Time.Advance(TimeSpan.FromSeconds(61));
        rig.Model.Refresh();
        rig.Model.CountdownValue.Should().Be("Starting now");

        rig.Time.Advance(TimeSpan.FromSeconds(60));
        rig.Model.Refresh();
        rig.Model.CountdownValue.Should().Be("Started 1 min ago");
        rig.Model.Tone.Should().Be(TugTone.Late);
    }

    [Fact]
    public void Refresh_with_nothing_new_raises_nothing()
    {
        var rig = new Rig(TimeSpan.FromSeconds(125));
        var changed = new List<string?>();
        rig.Model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Model.Refresh();

        changed.Should().BeEmpty();
    }

    [Theory]
    [InlineData(45, "Starts in 45 seconds")]
    [InlineData(1, "Starts in 1 second")]
    [InlineData(245, "Starts in 4 minutes 5 seconds")]
    [InlineData(60, "Starts in 1 minute")]
    [InlineData(3600, "Starts in 1 hour")]
    [InlineData(7500, "Starts in 2 hours 5 minutes")]
    [InlineData(0, "Starting now")]
    [InlineData(-30, "Starting now")]
    [InlineData(-190, "Started 3 minutes ago")]
    [InlineData(-3900, "Started 1 hour 5 minutes ago")]
    public void The_countdown_is_spoken_in_words(int secondsLeft, string expected)
    {
        new Rig(TimeSpan.FromSeconds(secondsLeft)).Model.SpokenCountdown.Should().Be(expected);
    }

    // ---- the details line ---------------------------------------------------------------------------------------

    [Fact]
    public void Details_are_the_time_range_the_calendar_and_the_people()
    {
        // Three other attendees and the person make four.
        new Rig(TimeSpan.FromMinutes(1)).Model.Details.Should().Be("12:01 – 12:31 PM · Work · 4 people");
    }

    [Fact]
    public void Details_leave_out_a_missing_calendar_title()
    {
        new Rig(calendar: null).Model.Details.Should().Be("12:04 – 12:34 PM · 4 people");
        new Rig(calendar: "").Model.Details.Should().Be("12:04 – 12:34 PM · 4 people");
    }

    [Fact]
    public void Details_leave_out_the_people_when_nobody_else_is_invited()
    {
        new Rig(others: 0).Model.Details.Should().Be("12:04 – 12:34 PM · Work");
        new Rig(others: 0, calendar: null).Model.Details.Should().Be("12:04 – 12:34 PM");
    }

    [Fact]
    public void Details_write_the_am_pm_marker_twice_only_when_the_ends_differ()
    {
        var rig = new Rig(TimeSpan.FromMinutes(-45) + TimeSpan.FromSeconds(0), length: TimeSpan.FromMinutes(90));
        // 11:15 AM to 12:45 PM
        rig.Model.Details.Should().Be("11:15 AM – 12:45 PM · Work · 4 people");
    }

    [Fact]
    public void Details_are_in_the_local_time_zone()
    {
        new Rig(plusFiveHours: true).Model.Details.Should().StartWith("7:04 – 7:34 AM");
    }

    [Fact]
    public void An_all_day_event_says_all_day_instead_of_a_range()
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        var time = new FakeTimeProvider();
        time.SetUtcNow(Noon);
        var request = new TakeoverRequest("e1", "Offsite", Noon.AddHours(-12), Noon.AddHours(12), true, true, null, Standard, "Work", 0);
        var model = new TugViewModel(Rig.PlanFor(request), TugMonitorRole.Primary, new FakeUiSettings(), new ReswLocalizer(), time, new RecordingResponder());

        model.Details.Should().Be("All day · Work");
    }

    // ---- hints -------------------------------------------------------------------------------------------------

    [Fact]
    public void Hints_list_every_key_that_does_something()
    {
        var model = new Rig().Model;

        model.Hints.Should().Be("Return joins · Esc dismisses · 1, 5 or 0 snoozes");
        model.SpokenHints.Should().Be("Return joins. Escape dismisses. 1 snoozes for 1 minute, 5 for 5 minutes, 0 for 10 minutes.");
    }

    [Fact]
    public void Hints_leave_out_join_when_there_is_no_link()
    {
        new Rig(joinUrl: null).Model.Hints.Should().Be("Esc dismisses · 1, 5 or 0 snoozes");
    }

    [Fact]
    public void Hints_leave_out_snooze_keys_that_are_not_offered()
    {
        new Rig(snooze: [TimeSpan.FromSeconds(300)]).Model.Hints.Should().Be("Return joins · Esc dismisses · 5 snoozes");
        new Rig(snooze: [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(600)]).Model.Hints.Should().Be("Return joins · Esc dismisses · 1 or 0 snoozes");
        new Rig(snooze: []).Model.Hints.Should().Be("Return joins · Esc dismisses");
        // A snooze length with no key (30 minutes) has no hint.
        new Rig(snooze: [TimeSpan.FromMinutes(30)]).Model.Hints.Should().Be("Return joins · Esc dismisses");
    }

    // ---- the snooze menu ---------------------------------------------------------------------------------------

    [Fact]
    public void The_snooze_menu_lists_the_requests_own_options_and_their_keys()
    {
        var model = new Rig(snooze: [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600), TimeSpan.FromMinutes(30)]).Model;

        model.SnoozeChoices.Select(c => (c.Title, c.Key)).Should().Equal(("1 minute", "1"), ("5 minutes", "5"), ("10 minutes", "0"), ("30 minutes", null));
        model.SnoozeChoices.Select(c => c.Duration).Should().Equal(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void No_snooze_options_means_no_snooze_choices()
    {
        new Rig(snooze: []).Model.SnoozeChoices.Should().BeEmpty();
    }

    // ---- keyboard ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Return_joins_when_there_is_a_link_and_dismisses_when_there_is_not()
    {
        var withLink = new Rig();
        (await withLink.Model.HandleKeyAsync(TugKey.Return)).Should().BeTrue();
        withLink.Responder.Calls.Should().Equal("join");

        var noLink = new Rig(joinUrl: null);
        (await noLink.Model.HandleKeyAsync(TugKey.Return)).Should().BeTrue();
        noLink.Responder.Calls.Should().Equal("dismiss");
    }

    [Fact]
    public async Task Escape_dismisses()
    {
        var rig = new Rig();

        (await rig.Model.HandleKeyAsync(TugKey.Escape)).Should().BeTrue();

        rig.Responder.Calls.Should().Equal("dismiss");
    }

    [Theory]
    [InlineData(TugKey.Digit1, 60)]
    [InlineData(TugKey.Digit5, 300)]
    [InlineData(TugKey.Digit0, 600)]
    public async Task A_number_key_snoozes_when_that_length_is_offered(TugKey key, int seconds)
    {
        var rig = new Rig();

        (await rig.Model.HandleKeyAsync(key)).Should().BeTrue();

        rig.Responder.Calls.Should().Equal("snooze:" + seconds);
    }

    [Theory]
    [InlineData(TugKey.Digit1)]
    [InlineData(TugKey.Digit0)]
    public async Task A_number_key_does_nothing_when_that_length_is_not_offered(TugKey key)
    {
        var rig = new Rig(snooze: [TimeSpan.FromSeconds(300)]);

        (await rig.Model.HandleKeyAsync(key)).Should().BeFalse();

        rig.Responder.Calls.Should().BeEmpty();
        rig.Model.Resolve(key).Should().BeNull();
    }

    [Fact]
    public void Resolve_names_the_response_without_doing_it()
    {
        var rig = new Rig();

        rig.Model.Resolve(TugKey.Return).Should().Be(new TugCommand.Join());
        rig.Model.Resolve(TugKey.Escape).Should().Be(new TugCommand.Dismiss());
        rig.Model.Resolve(TugKey.Digit5).Should().Be(new TugCommand.Snooze(TimeSpan.FromSeconds(300)));
        rig.Responder.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_mirror_has_no_keyboard_and_no_controls()
    {
        var rig = new Rig(role: TugMonitorRole.Mirror);

        foreach (var key in Enum.GetValues<TugKey>())
        {
            (await rig.Model.HandleKeyAsync(key)).Should().BeFalse();
        }

        rig.Responder.Calls.Should().BeEmpty();
        rig.Model.IsPrimary.Should().BeFalse();
        rig.Model.MirrorHint.Should().Be("Dismiss on your main display");
    }

    [Fact]
    public async Task The_buttons_call_the_controller()
    {
        var rig = new Rig();

        await rig.Model.JoinAsync();
        await rig.Model.SnoozeAsync(TimeSpan.FromSeconds(300));
        await rig.Model.DismissAsync();

        rig.Responder.Calls.Should().Equal("join", "snooze:300", "dismiss");
    }

    [Fact]
    public async Task Join_without_a_link_does_nothing()
    {
        var rig = new Rig(joinUrl: null);

        rig.Model.HasJoin.Should().BeFalse();
        await rig.Model.JoinAsync();

        rig.Responder.Calls.Should().BeEmpty();
    }

    // ---- reduced motion, contrast, transparency -----------------------------------------------------------------

    [Fact]
    public void The_urgent_countdown_pulses_only_when_animations_are_on()
    {
        var rig = new Rig(TimeSpan.FromSeconds(30));
        rig.Model.ReducedMotion.Should().BeFalse();
        rig.Model.PulseEnabled.Should().BeTrue();

        rig.Settings.AnimationsEnabled = false;
        rig.Model.RefreshSettings();

        rig.Model.ReducedMotion.Should().BeTrue();
        rig.Model.PulseEnabled.Should().BeFalse();
    }

    [Fact]
    public void Only_an_urgent_countdown_pulses_not_a_calm_one_nor_a_late_one()
    {
        new Rig(TimeSpan.FromSeconds(245)).Model.PulseEnabled.Should().BeFalse();
        new Rig(TimeSpan.FromSeconds(-30)).Model.PulseEnabled.Should().BeFalse();
        new Rig(TimeSpan.FromSeconds(-300)).Model.PulseEnabled.Should().BeFalse();
    }

    [Fact]
    public void A_contrast_theme_replaces_the_brand_palette_and_the_backdrop()
    {
        var rig = new Rig();
        rig.Model.Palette.Should().Be(TugPalette.Brand);
        rig.Model.Backdrop.Should().Be(TugBackdropKind.Acrylic);

        rig.Settings.HighContrast = true;
        rig.Model.RefreshSettings();

        rig.Model.Palette.Should().Be(TugPalette.SystemContrast);
        rig.Model.Backdrop.Should().Be(TugBackdropKind.SystemContrast);
    }

    [Fact]
    public void Transparency_off_gives_solid_navy_and_a_contrast_theme_wins_over_it()
    {
        var rig = new Rig();
        rig.Settings.TransparencyEffectsEnabled = false;
        rig.Model.RefreshSettings();
        rig.Model.Backdrop.Should().Be(TugBackdropKind.SolidNavy);
        rig.Model.Palette.Should().Be(TugPalette.Brand);

        rig.Settings.HighContrast = true;
        rig.Model.RefreshSettings();
        rig.Model.Backdrop.Should().Be(TugBackdropKind.SystemContrast);
    }

    [Fact]
    public void Changing_a_setting_raises_the_matching_property_changes()
    {
        var rig = new Rig(TimeSpan.FromSeconds(30));
        var changed = new List<string?>();
        rig.Model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Settings.AnimationsEnabled = false;
        rig.Settings.TransparencyEffectsEnabled = false;
        rig.Model.RefreshSettings();

        changed.Should().Contain([nameof(TugViewModel.ReducedMotion), nameof(TugViewModel.PulseEnabled), nameof(TugViewModel.Backdrop)]);
    }

    // ---- the announcement --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(245, "Design review starts in 4 minutes")]
    [InlineData(45, "Design review starts in less than a minute")]
    [InlineData(0, "Design review is starting now")]
    [InlineData(-190, "Design review started 3 minutes ago")]
    public void The_announcement_names_the_meeting_and_when(int secondsLeft, string expected)
    {
        new Rig(TimeSpan.FromSeconds(secondsLeft)).Model.Announcement.Should().Be(expected);
    }

    [Fact]
    public void The_window_title_carries_the_announcement_so_narrator_reads_it_on_activation()
    {
        new Rig(TimeSpan.FromSeconds(245)).Model.WindowTitle.Should().Be("TimeTug: Design review starts in 4 minutes");
    }

    // ---- plan updates ------------------------------------------------------------------------------------------

    [Fact]
    public void The_click_to_respond_banner_and_the_failed_link_follow_the_plan()
    {
        var rig = new Rig();
        rig.Model.ShowClickToRespond.Should().BeFalse();
        rig.Model.HasJoinFailed.Should().BeFalse();
        var changed = new List<string?>();
        rig.Model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Model.Update(Rig.PlanFor(rig.Request, clickToRespond: true, joinFailed: "https://meet.example.test/abc"));

        rig.Model.ShowClickToRespond.Should().BeTrue();
        rig.Model.JoinFailedUrl.Should().Be("https://meet.example.test/abc");
        rig.Model.HasJoinFailed.Should().BeTrue();
        changed.Should().Contain([nameof(TugViewModel.ShowClickToRespond), nameof(TugViewModel.JoinFailedUrl), nameof(TugViewModel.HasJoinFailed)]);

        rig.Model.Update(Rig.PlanFor(rig.Request));
        rig.Model.ShowClickToRespond.Should().BeFalse();
        rig.Model.HasJoinFailed.Should().BeFalse();
    }

    [Fact]
    public void The_title_is_the_meeting_title_and_the_join_button_is_named_for_screen_readers()
    {
        var model = new Rig().Model;

        model.Title.Should().Be("Design review");
        model.HasJoin.Should().BeTrue();
    }

    [Fact]
    public void A_plan_without_a_request_is_refused()
    {
        var act = () => new TugViewModel(TugPlan.None, TugMonitorRole.Primary, new FakeUiSettings(), new ReswLocalizer(), new FakeTimeProvider(), new RecordingResponder());

        act.Should().Throw<ArgumentException>();
    }
}
