using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Tests.Tray;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Flyout;

public sealed class FlyoutViewModelTests : IDisposable
{
    /// <summary>Friday 2026-10-16, noon UTC.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeEngineClient _engine = new();
    private readonly FakeLauncher _launcher = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FakeMinuteTicker _ticker = new();
    private readonly FlyoutViewModel _model;
    private int _changes;

    public FlyoutViewModelTests()
    {
        _time.SetUtcNow(Noon);
        _model = new FlyoutViewModel(_engine, _launcher, _time, _ticker, new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"));
        _model.Changed += () => _changes++;
    }

    public void Dispose() => _model.Dispose();

    private static AgendaItem Item(
        string id,
        string title,
        int startMinutes,
        int lengthMinutes,
        AgendaPhase phase,
        string? calendar = "Work",
        string? color = "#3366CC",
        string? join = null,
        int others = 0,
        bool next = false) =>
        new(id, title, Noon.AddMinutes(startMinutes), Noon.AddMinutes(startMinutes + lengthMinutes), false, phase, calendar, color,
            join is null ? null : new Uri(join), others, false, null)
        {
            IsNext = next,
        };

    private static AgendaItem AllDay(string id, string title, string? calendar = "Personal") =>
        new(id, title, new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero), true,
            AgendaPhase.Upcoming, calendar, null, null, 0, false, null)
        {
            StartDate = new DateOnly(2026, 10, 16),
            EndDate = new DateOnly(2026, 10, 17),
        };

    private static AppState State(
        IReadOnlyList<AgendaItem>? agenda = null,
        IReadOnlyList<SourceProblem>? problems = null,
        TimeSpan? lead = null) =>
        States.Empty() with
        {
            Agenda = agenda ?? [],
            Problems = problems ?? [],
            LeadTime = lead ?? TimeSpan.FromSeconds(60),
        };

    /// <summary>A day with a finished, a current and two upcoming meetings, one of them all day.</summary>
    private static AppState Day() => State(
    [
        AllDay("holiday", "Holiday"),
        Item("past", "Standup", -180, 30, AgendaPhase.Past),
        Item("now", "Design review", -20, 30, AgendaPhase.Current, join: "https://zoom.us/j/1"),
        Item("next", "Planning", 180, 60, AgendaPhase.Upcoming, calendar: "Personal", color: "#00FF00", join: "https://meet.google.com/aaa-bbbb-ccc", next: true),
        Item("later", "Wind down", 300, 90, AgendaPhase.Upcoming, calendar: null, color: null, join: "https://example.com/room"),
    ]);

    // ---- header -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_header_has_the_weekday_and_how_many_meetings_are_left()
    {
        _engine.Push(Day());

        _model.Weekday.Should().Be("Friday");
        _model.Summary.Should().Be("Oct 16 · 3 meetings left", "the current meeting and the two upcoming ones count; the finished one and the all-day one do not");
    }

    [Fact]
    public void One_meeting_left_is_singular()
    {
        _engine.Push(State([Item("a", "A", 60, 30, AgendaPhase.Upcoming, next: true), Item("b", "B", -120, 30, AgendaPhase.Past)]));

        _model.Summary.Should().Be("Oct 16 · 1 meeting left");
    }

    [Fact]
    public void A_day_with_no_meetings_says_so_and_shows_the_empty_state()
    {
        _engine.Push(State());

        _model.Summary.Should().Be("Oct 16 · No meetings today");
        _model.IsEmpty.Should().BeTrue();
        _model.NoMoreMeetings.Should().BeFalse();
        _model.Rows.Should().BeEmpty();
        (_model.EmptyTitle, _model.EmptySubtitle).Should().Be(("No meetings today", "Enjoy the quiet."));
    }

    [Fact]
    public void After_the_last_meeting_the_summary_and_a_banner_say_none_are_left()
    {
        _engine.Push(State([Item("a", "A", -120, 30, AgendaPhase.Past), Item("b", "B", -60, 30, AgendaPhase.Past)]));

        _model.Summary.Should().Be("Oct 16 · No meetings left");
        _model.NoMoreMeetings.Should().BeTrue();
        _model.IsEmpty.Should().BeFalse();
        _model.NoMoreMeetingsText.Should().Be("No more meetings today. Nice.");
    }

    [Fact]
    public void A_day_of_only_all_day_events_counts_no_timed_meetings_as_the_Mac_does()
    {
        _engine.Push(State([AllDay("h", "Holiday")]));

        _model.Summary.Should().Be("Oct 16 · No meetings today");
        _model.IsEmpty.Should().BeFalse("there is a row to show");
        _model.NoMoreMeetings.Should().BeTrue();
        _model.Rows.Should().ContainSingle().Which.Kind.Should().Be(FlyoutRowKind.AllDay);
    }

    [Fact]
    public void Nothing_is_called_empty_before_the_engines_first_state()
    {
        _model.IsEmpty.Should().BeFalse();
        _model.NoMoreMeetings.Should().BeFalse();
        _model.Rows.Should().BeEmpty();
    }

    // ---- the cards ----------------------------------------------------------------------------------------------

    [Fact]
    public void There_is_one_row_per_agenda_item_in_the_engines_order()
    {
        _engine.Push(Day());

        _model.Rows.Select(r => r.EventId).Should().Equal("holiday", "past", "now", "next", "later");
        _model.Rows.Select(r => r.Kind).Should().Equal(
            FlyoutRowKind.AllDay, FlyoutRowKind.Past, FlyoutRowKind.Current, FlyoutRowKind.Next, FlyoutRowKind.Upcoming);
    }

    [Fact]
    public void A_finished_meeting_shows_its_start_and_how_long_it_took()
    {
        _engine.Push(Day());

        var row = _model.Rows.Single(r => r.EventId == "past");

        row.TimeText.Should().Be("9:00 AM");
        row.MetaText.Should().Be("30 min · done");
        row.IsPast.Should().BeTrue();
        row.BadgeText.Should().BeNull();
    }

    [Fact]
    public void The_meeting_in_progress_says_now_when_it_ends_and_how_far_along_it_is()
    {
        _engine.Push(Day());

        var row = _model.Rows.Single(r => r.EventId == "now");

        row.TimeText.Should().Be("11:40 AM");
        row.MetaText.Should().Be("Ends 12:10 PM");
        row.BadgeText.Should().Be("Now");
        row.IsCurrent.Should().BeTrue();
        row.Progress.Should().BeApproximately(20.0 / 30, 0.0001);
    }

    [Fact]
    public void The_next_meeting_shows_its_range_calendar_and_how_long_until_it_starts()
    {
        _engine.Push(Day());

        var row = _model.Rows.Single(r => r.EventId == "next");

        row.TimeText.Should().Be("3:00 PM");
        row.MetaText.Should().Be("3:00 – 4:00 PM · Personal");
        row.BadgeText.Should().Be("in 3h 0m");
        row.IsNext.Should().BeTrue();
        row.CalendarColorHex.Should().Be("#00FF00");
    }

    [Fact]
    public void A_later_meeting_has_a_range_and_no_badge_and_a_missing_calendar_leaves_the_range_bare()
    {
        _engine.Push(Day());

        var row = _model.Rows.Single(r => r.EventId == "later");

        row.MetaText.Should().Be("5:00 – 6:30 PM");
        row.BadgeText.Should().BeNull();
        row.CalendarColorHex.Should().BeNull();
        row.Kind.Should().Be(FlyoutRowKind.Upcoming);
    }

    [Fact]
    public void An_all_day_event_says_all_day_with_its_calendar()
    {
        _engine.Push(Day());

        var row = _model.Rows.Single(r => r.EventId == "holiday");

        row.TimeText.Should().Be("All day");
        row.MetaText.Should().Be("All day · Personal");
        row.HasJoin.Should().BeFalse();
    }

    [Fact]
    public void An_all_day_event_is_all_day_even_when_it_is_the_one_the_engine_calls_next()
    {
        _engine.Push(State([AllDay("h", "Holiday") with { IsNext = true }]));

        _model.Rows.Single().Kind.Should().Be(FlyoutRowKind.AllDay);
    }

    [Fact]
    public void A_merged_meeting_shows_its_longer_range_and_says_what_was_merged()
    {
        var merged = Item("m", "Sync", 60, 45, AgendaPhase.Upcoming, next: true) with
        {
            ShownStart = Noon.AddMinutes(30),
            IsMerged = true,
            MergedEventCount = 4,
            MergeKind = "inference",
            MergeEngineName = "Apple Intelligence",
        };
        _engine.Push(State([merged]));

        var row = _model.Rows.Single();

        row.TimeText.Should().Be("12:30 PM");
        row.MetaText.Should().Be("12:30 – 1:45 PM · Work");
        row.BadgeText.Should().Be("in 1h 0m", "the countdown follows the tug time, not the shown start");
        row.MergeSummary.Should().Be("Merged with Apple Intelligence · 4 events");
    }

    [Theory]
    [InlineData("userConfirmed", 2, "Merged manually · 2 events")]
    [InlineData("rule", 3, "3 events merged")]
    [InlineData(null, 3, "3 events merged")]
    public void The_merge_summary_follows_how_the_merge_was_decided(string? kind, int count, string expected)
    {
        var merged = Item("m", "Sync", 60, 45, AgendaPhase.Upcoming) with { IsMerged = true, MergedEventCount = count, MergeKind = kind };
        _engine.Push(State([merged]));

        _model.Rows.Single().MergeSummary.Should().Be(expected);
    }

    [Fact]
    public void An_event_that_is_not_a_merge_has_no_summary()
    {
        _engine.Push(Day());

        _model.Rows.Should().OnlyContain(r => r.MergeSummary == null);
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "1 other")]
    [InlineData(5, "5 others")]
    public void Attendees_are_written_as_others(int others, string? expected)
    {
        _engine.Push(State([Item("a", "A", 60, 30, AgendaPhase.Upcoming, others: others)]));

        _model.Rows.Single().AttendeeText.Should().Be(expected);
    }

    [Fact]
    public void Join_is_offered_only_on_the_meeting_in_progress_and_the_next_one_and_only_with_a_link()
    {
        _engine.Push(Day());

        _model.Rows.ToDictionary(r => r.EventId, r => r.HasJoin).Should().BeEquivalentTo(new Dictionary<string, bool>
        {
            ["holiday"] = false,
            ["past"] = false,
            ["now"] = true,
            ["next"] = true,
            ["later"] = false,
        });
        _model.Rows.Single(r => r.EventId == "now").JoinLabel.Should().Be("Join Zoom");
        _model.Rows.Single(r => r.EventId == "next").JoinLabel.Should().Be("Join Google Meet");
        _model.Rows.Single(r => r.EventId == "now").JoinAutomationName.Should().Be("Join Design review");
    }

    [Fact]
    public void A_current_or_next_meeting_without_a_link_has_no_join()
    {
        _engine.Push(State([Item("a", "A", -10, 30, AgendaPhase.Current), Item("b", "B", 60, 30, AgendaPhase.Upcoming, next: true)]));

        _model.Rows.Should().OnlyContain(r => !r.HasJoin);
    }

    [Fact]
    public void Each_card_has_a_spoken_description()
    {
        _engine.Push(Day());

        _model.Rows.Single(r => r.EventId == "holiday").AutomationName.Should().Be("Holiday, all day, Personal calendar");
        _model.Rows.Single(r => r.EventId == "past").AutomationName.Should().Be("Standup, 9:00 to 9:30 AM, finished, Work calendar");
        _model.Rows.Single(r => r.EventId == "now").AutomationName.Should().Be("Design review, 11:40 AM to 12:10 PM, in progress, Work calendar");
        _model.Rows.Single(r => r.EventId == "next").AutomationName.Should().Be("Planning, 3:00 to 4:00 PM, in 3 hours, Personal calendar");
        _model.Rows.Single(r => r.EventId == "later").AutomationName.Should().Be("Wind down, 5:00 to 6:30 PM");
    }

    [Fact]
    public void Times_are_shown_in_the_local_time_zone_of_the_time_provider()
    {
        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(-5), "test", "test"));
        _engine.Push(Day());

        _model.Rows.Single(r => r.EventId == "next").TimeText.Should().Be("10:00 AM");
        _model.Weekday.Should().Be("Friday");
    }

    // ---- Join ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Join_opens_the_link_and_tells_the_engine_nothing()
    {
        _engine.Push(Day());

        await _model.JoinAsync("now");

        _launcher.Opened.Should().Equal("https://zoom.us/j/1");
        _engine.Sent.Should().BeEmpty("the Mac's popup only opens the link; it does not answer a takeover");
        _model.Rows.Single(r => r.EventId == "now").HasJoinFailed.Should().BeFalse();
    }

    [Fact]
    public async Task A_link_that_cannot_be_opened_is_kept_on_the_row_so_the_person_can_copy_it()
    {
        _engine.Push(Day());
        _launcher.Succeeds = false;
        var before = _changes;

        await _model.JoinAsync("next");

        var row = _model.Rows.Single(r => r.EventId == "next");
        row.JoinFailedUrl.Should().Be("https://meet.google.com/aaa-bbbb-ccc");
        row.JoinFailedMessage.Should().Contain("couldn’t open the meeting link");
        _engine.Sent.Should().BeEmpty();
        _changes.Should().BeGreaterThan(before);
        _model.Rows.Single(r => r.EventId == "now").HasJoinFailed.Should().BeFalse();
    }

    [Fact]
    public async Task A_launcher_that_throws_counts_as_a_failure_and_never_escapes()
    {
        var model = new FlyoutViewModel(_engine, new ThrowingLauncher(), _time, _ticker, new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"));
        _engine.Push(Day());

        var act = async () => await model.JoinAsync("now");

        await act.Should().NotThrowAsync();
        model.Rows.Single(r => r.EventId == "now").HasJoinFailed.Should().BeTrue();
        model.Dispose();
    }

    [Fact]
    public async Task A_later_successful_join_clears_the_failure()
    {
        _engine.Push(Day());
        _launcher.Succeeds = false;
        await _model.JoinAsync("now");
        _launcher.Succeeds = true;

        await _model.JoinAsync("now");

        _model.Rows.Single(r => r.EventId == "now").HasJoinFailed.Should().BeFalse();
    }

    [Fact]
    public async Task The_failure_stays_through_a_refresh_and_goes_when_the_event_leaves_the_agenda()
    {
        _engine.Push(Day());
        _launcher.Succeeds = false;
        await _model.JoinAsync("now");

        _engine.Push(Day());
        _model.Rows.Single(r => r.EventId == "now").HasJoinFailed.Should().BeTrue();

        _engine.Push(State([Item("other", "Other", 60, 30, AgendaPhase.Upcoming)]));
        _engine.Push(Day());
        _model.Rows.Single(r => r.EventId == "now").HasJoinFailed.Should().BeFalse();
    }

    [Theory]
    [InlineData("past")]
    [InlineData("later")]
    [InlineData("holiday")]
    [InlineData("nothing-like-this")]
    public async Task Join_on_a_row_without_a_join_button_does_nothing(string eventId)
    {
        _engine.Push(Day());

        await _model.JoinAsync(eventId);

        _launcher.Opened.Should().BeEmpty();
        _engine.Sent.Should().BeEmpty();
    }

    // ---- problems -----------------------------------------------------------------------------------------------

    [Fact]
    public void Each_kind_of_problem_has_the_Macs_wording()
    {
        _engine.Push(State(problems:
        [
            new SourceProblem("caldav-2", "Work CalDAV", ProblemKind.Failing, "Server unreachable", "2"),
            new SourceProblem("eventkit", "System calendar", ProblemKind.NeedsPermission, null, null),
            new SourceProblem("google-1", "me@example.com", ProblemKind.AuthExpired, null, "1"),
            new SourceProblem("icloud-4", "Home", ProblemKind.Failing, null, "4"),
        ]));

        _model.Problems.Select(p => p.Message).Should().Equal(
            "Work CalDAV failed: Server unreachable",
            "System calendar access denied",
            "me@example.com needs you to sign in again",
            "Home failed");
    }

    [Fact]
    public void Only_an_expired_sign_in_with_a_known_account_offers_to_sign_in_again()
    {
        _engine.Push(State(problems:
        [
            new SourceProblem("caldav-2", "Work CalDAV", ProblemKind.Failing, "x", "2"),
            new SourceProblem("eventkit", "System calendar", ProblemKind.NeedsPermission, null, null),
            new SourceProblem("google-1", "me@example.com", ProblemKind.AuthExpired, null, "1"),
            new SourceProblem("ms-1", "ms-1", ProblemKind.AuthExpired, null, null),
        ]));

        _model.Problems.Select(p => p.ActionText).Should().Equal(null, null, "Sign in again", null);
        _model.Problems.Single(p => p.HasAction).ActionAutomationName.Should().Be("Sign in to me@example.com again");
        _model.Problems.Single(p => p.HasAction).ConnectionId.Should().Be("1");
    }

    [Fact]
    public async Task Sign_in_again_asks_the_engine_to_reconnect_that_account()
    {
        _engine.Push(State(problems: [new SourceProblem("google-1", "me@example.com", ProblemKind.AuthExpired, null, "1")]));

        await _model.ReconnectAsync("1");

        _engine.Sent.Should().Equal("reconnect:1");
    }

    [Fact]
    public async Task A_stale_or_made_up_account_id_sends_nothing()
    {
        _engine.Push(State(problems: [new SourceProblem("google-1", "me@example.com", ProblemKind.AuthExpired, null, "1")]));

        await _model.ReconnectAsync("9");
        _engine.Push(State());
        await _model.ReconnectAsync("1");

        _engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public void No_problems_means_no_problem_rows()
    {
        _engine.Push(Day());

        _model.Problems.Should().BeEmpty();
    }

    // ---- footer -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_footer_says_how_long_before_a_meeting_the_tug_fires()
    {
        _engine.Push(State(lead: TimeSpan.FromSeconds(60)));
        _model.Footer.Should().Be("Tugs you 1 min before");

        _engine.Push(State(lead: TimeSpan.FromMinutes(5)));
        _model.Footer.Should().Be("Tugs you 5 min before");

        _engine.Push(State(lead: TimeSpan.Zero));
        _model.Footer.Should().Be("Tugs you at start");
    }

    // ---- staying current ----------------------------------------------------------------------------------------

    [Fact]
    public void A_new_state_replaces_the_rows_and_raises_Changed()
    {
        _engine.Push(State());
        var before = _changes;

        _engine.Push(Day());

        _model.Rows.Should().HaveCount(5);
        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public void A_state_that_changes_nothing_shown_raises_nothing()
    {
        _engine.Push(Day());
        var before = _changes;

        _engine.Push(Day());

        _changes.Should().Be(before);
    }

    [Fact]
    public void A_phase_change_from_the_engine_moves_the_row_from_upcoming_to_current()
    {
        _engine.Push(State([Item("a", "A", 5, 30, AgendaPhase.Upcoming, next: true)]));
        _model.Rows.Single().Kind.Should().Be(FlyoutRowKind.Next);

        _engine.Push(State([Item("a", "A", 5, 30, AgendaPhase.Current)]));

        _model.Rows.Single().Kind.Should().Be(FlyoutRowKind.Current);
    }

    [Fact]
    public void The_clock_alone_never_changes_a_phase()
    {
        _engine.Push(State([Item("a", "A", 5, 30, AgendaPhase.Upcoming, next: true)]));

        _time.Advance(TimeSpan.FromHours(3));
        _ticker.Tick();

        _model.Rows.Single().Kind.Should().Be(FlyoutRowKind.Next, "only the engine moves a meeting between phases; the minute tick refreshes the wording");
    }

    [Fact]
    public void The_minute_tick_refreshes_the_relative_times()
    {
        _engine.Push(Day());
        _model.Rows.Single(r => r.EventId == "next").BadgeText.Should().Be("in 3h 0m");
        var before = _changes;

        _time.Advance(TimeSpan.FromMinutes(1));
        _ticker.Tick();

        _model.Rows.Single(r => r.EventId == "next").BadgeText.Should().Be("in 2h 59m");
        _model.Rows.Single(r => r.EventId == "now").MetaText.Should().Be("Ends 12:10 PM");
        _model.Rows.Single(r => r.EventId == "now").Progress.Should().BeApproximately(21.0 / 30, 0.0001);
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
    public void The_weekday_and_date_roll_over_at_midnight_on_the_next_tick()
    {
        _engine.Push(State());

        _time.SetUtcNow(new DateTimeOffset(2026, 10, 17, 0, 0, 30, TimeSpan.Zero));
        _ticker.Tick();

        _model.Weekday.Should().Be("Saturday");
        _model.Summary.Should().Be("Oct 17 · No meetings today");
    }

    [Fact]
    public void A_clock_change_is_picked_up_on_the_next_tick_and_the_phase_still_waits_for_the_engine()
    {
        _engine.Push(Day());

        _time.SetUtcNow(Noon.AddHours(2));
        _ticker.Tick();

        _model.Rows.Single(r => r.EventId == "next").BadgeText.Should().Be("in 1h 0m");
        var current = _model.Rows.Single(r => r.EventId == "now");
        current.Progress.Should().Be(1, "the clock is past the meeting's end, but the engine has not moved it out of 'current'");
        current.Kind.Should().Be(FlyoutRowKind.Current);
    }

    [Fact]
    public void A_time_zone_change_is_picked_up_on_the_next_tick()
    {
        _engine.Push(Day());

        _time.SetLocalTimeZone(TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(2), "test", "test"));
        _ticker.Tick();

        _model.Rows.Single(r => r.EventId == "next").TimeText.Should().Be("5:00 PM");
    }

    [Fact]
    public void One_ticker_runs_and_disposing_stops_it_and_the_listening()
    {
        _ticker.Started.Should().Be(1);

        _model.Dispose();
        var before = _changes;
        _engine.Push(Day());

        _ticker.Stopped.Should().Be(1);
        _changes.Should().Be(before);
    }

    [Fact]
    public void A_state_that_arrived_before_the_view_model_is_used_at_once()
    {
        _engine.Push(Day());

        using var late = new FlyoutViewModel(_engine, _launcher, _time, new FakeMinuteTicker(), new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"));

        late.Rows.Should().HaveCount(5);
    }

    private sealed class ThrowingLauncher : TimeTug.Presentation.Abstractions.IUrlLauncher
    {
        public ValueTask<bool> LaunchAsync(string url) => throw new InvalidOperationException("shell failure");
    }
}
