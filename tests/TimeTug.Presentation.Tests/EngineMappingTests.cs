using System.Text.Json;
using System.Text.Json.Nodes;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests;

public class EngineMappingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static Pkg.CalendarEvent Meeting(string id, bool link) => new()
    {
        Id = id,
        Title = "Standup " + id,
        Start = Start,
        End = Start.AddMinutes(30),
        Conferences = link ? [new Pkg.Conference { Url = "https://meet.example/" + id, Provider = "meet" }] : [],
    };

    private static Pkg.EngineState Full() => new()
    {
        Events = [Meeting("e1", true), Meeting("e2", false)],
        Agenda = new Pkg.Agenda { NextEventID = "e2" },
        Accounts =
        [
            new Pkg.Account { KindID = "google", ConnectionID = "1", DisplayName = "me@example.com" },
            new Pkg.Account { KindID = "caldav", ConnectionID = "2", DisplayName = "Work CalDAV" },
            new Pkg.Account { KindID = "microsoft", ConnectionID = "3", DisplayName = "me@work.example" },
        ],
        SourceNames = new Dictionary<string, string> { ["google-1"] = "me@example.com", ["caldav-2"] = "Work CalDAV", ["microsoft-3"] = "me@work.example" },
        Calendars =
        [
            new Pkg.CalendarInfo { Key = "google-1/a", SourceID = "google-1" },
            new Pkg.CalendarInfo { Key = "google-1/b", SourceID = "google-1" },
            new Pkg.CalendarInfo { Key = "google-1/c", SourceID = "google-1" },
            new Pkg.CalendarInfo { Key = "caldav-2/a", SourceID = "caldav-2" },
        ],
        Statuses = new Dictionary<string, Pkg.SourceStatus>
        {
            ["google-1"] = new Pkg.SourceStatus { Kind = "ok" },
            ["microsoft-3"] = new Pkg.SourceStatus { Kind = "authExpired" },
        },
        AccountStatus = new Pkg.AccountStatus { BuildFailures = new Dictionary<string, string> { ["2"] = "Could not connect" } },
        AvailableAccountKinds =
        [
            new Pkg.AccountKind { Id = "google", DisplayName = "Google", IsConfigured = true },
            new Pkg.AccountKind { Id = "microsoft", DisplayName = "Microsoft", IsConfigured = false },
        ],
        NextTakeover = new Pkg.NextTakeover { EventID = "e2", FireAt = Start.AddMinutes(-1) },
        TakeoverVisible = true,
        Settings = new Pkg.EngineSettings { Takeover = new Pkg.TakeoverSettings { Enabled = false } },
    };

    [Fact]
    public void Accounts_carry_id_kind_name_status_and_calendar_count()
    {
        var accounts = EngineMapping.ToAppState(Full()).Accounts;
        accounts.Should().Equal(
            new AccountInfo("1", "google", "me@example.com", "ok", 3),
            new AccountInfo("2", "caldav", "Work CalDAV", "failing", 1) { BuildFailure = true },
            new AccountInfo("3", "microsoft", "me@work.example", "authExpired", 0));
    }

    [Fact]
    public void Only_a_build_failure_marks_the_account_as_one_that_cannot_start()
    {
        var accounts = EngineMapping.ToAppState(Full()).Accounts;
        accounts.Where(a => a.BuildFailure).Select(a => a.ConnectionId).Should().Equal("2");
    }

    [Fact]
    public void An_account_whose_source_has_no_status_yet_is_still_connecting()
    {
        var state = Full() with { Statuses = new Dictionary<string, Pkg.SourceStatus> { ["microsoft-3"] = new Pkg.SourceStatus { Kind = "authExpired" } } };
        var accounts = EngineMapping.ToAppState(state).Accounts;
        accounts.Single(a => a.ConnectionId == "1").StatusKnown.Should().BeFalse();
        accounts.Single(a => a.ConnectionId == "3").StatusKnown.Should().BeTrue();
        accounts.Single(a => a.ConnectionId == "2").StatusKnown.Should().BeTrue("a build failure is a status of its own");
    }

    [Fact]
    public void Unreadable_repeating_events_are_counted_per_account_and_other_notices_are_ignored()
    {
        var state = Full() with
        {
            Notices = new Dictionary<string, IReadOnlyList<Pkg.SourceNotice>>
            {
                ["google-1"] = [new Pkg.SourceNotice { Kind = "unreadableRecurrence", Count = 2 }, new Pkg.SourceNotice { Kind = "somethingElse", Count = 9 }],
                ["caldav-2"] = [new Pkg.SourceNotice { Kind = "unreadableRecurrence", Count = 1 }, new Pkg.SourceNotice { Kind = "unreadableRecurrence", Count = 3 }],
            },
        };
        var accounts = EngineMapping.ToAppState(state).Accounts;
        accounts.Select(a => a.UnreadableRecurrences).Should().Equal(2, 4, 0);
    }

    [Fact]
    public void An_account_without_a_known_source_is_ok_with_no_calendars()
    {
        var state = new Pkg.EngineState { Accounts = [new Pkg.Account { KindID = "icloud", ConnectionID = "9", DisplayName = "Home" }] };
        EngineMapping.ToAppState(state).Accounts.Should().Equal(new AccountInfo("9", "icloud", "Home", "ok", 0) { StatusKnown = false });
    }

    [Fact]
    public void Available_kinds_keep_engine_order_and_configured_flag()
    {
        EngineMapping.ToAppState(Full()).AvailableKinds.Should().Equal(
            new AccountKindInfo("google", "Google", true),
            new AccountKindInfo("microsoft", "Microsoft", false));
    }

    [Fact]
    public void Events_carry_id_title_start_end_and_join_link_flag()
    {
        EngineMapping.ToAppState(Full()).Events.Should().Equal(
            new EventSummary("e1", "Standup e1", Start, Start.AddMinutes(30), false, true),
            new EventSummary("e2", "Standup e2", Start, Start.AddMinutes(30), false, false));
    }

    [Fact]
    public void Next_meeting_is_the_agenda_event_the_engine_names()
    {
        EngineMapping.ToAppState(Full()).NextMeeting.Should().Be(new NextMeetingInfo("Standup e2", Start, false));
    }

    [Fact]
    public void No_next_meeting_when_the_engine_names_none_or_an_unknown_event()
    {
        EngineMapping.ToAppState(new Pkg.EngineState()).NextMeeting.Should().BeNull();
        EngineMapping.ToAppState(new Pkg.EngineState { Agenda = new Pkg.Agenda { NextEventID = "gone" } }).NextMeeting.Should().BeNull();
    }

    [Fact]
    public void Next_takeover_comes_straight_from_the_engine()
    {
        EngineMapping.ToAppState(Full()).NextTakeover.Should().Be(new NextTakeoverInfo("e2", Start.AddMinutes(-1)));
        EngineMapping.ToAppState(new Pkg.EngineState()).NextTakeover.Should().BeNull();
    }

    [Fact]
    public void Takeover_visible_and_enabled_come_from_the_state()
    {
        var mapped = EngineMapping.ToAppState(Full());
        mapped.TakeoverVisible.Should().BeTrue();
        mapped.Enabled.Should().BeFalse();
    }

    [Fact]
    public void A_state_whose_takeover_settings_are_null_maps_to_the_defaults_instead_of_throwing()
    {
        var state = new Pkg.EngineState { Settings = new Pkg.EngineSettings { Takeover = null! } };

        var map = () => EngineMapping.ToAppState(state);

        var mapped = map.Should().NotThrow().Subject;
        var expected = EngineSettingsMapping.ToView(state.Settings);
        mapped.Enabled.Should().Be(expected.Enabled, "the Enabled the state carries is the mapped one, whichever way a missing Takeover reads");
        mapped.LeadTime.Should().Be(expected.LeadTime);
        mapped.Settings.SameAs(expected).Should().BeTrue();
    }

    [Fact]
    public void An_all_day_event_spans_the_local_midnights_of_its_dates()
    {
        var holiday = new Pkg.CalendarEvent
        {
            Id = "h1",
            Title = "Holiday",
            IsAllDay = true,
            StartDate = new DateOnly(2026, 10, 16),
            EndDate = new DateOnly(2026, 10, 17),
            TimeZone = "UTC",
        };
        var summary = EngineMapping.ToSummary(holiday);
        summary.IsAllDay.Should().BeTrue();
        summary.Start.Should().Be(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero));
        summary.End.Should().Be(new DateTimeOffset(2026, 10, 17, 0, 0, 0, TimeSpan.Zero));
        summary.HasJoinLink.Should().BeFalse();
    }

    [Fact]
    public void An_all_day_event_in_another_zone_uses_that_zones_midnight()
    {
        var holiday = new Pkg.CalendarEvent
        {
            Id = "h2",
            IsAllDay = true,
            StartDate = new DateOnly(2026, 10, 16),
            EndDate = new DateOnly(2026, 10, 17),
            TimeZone = "Europe/Berlin",
        };
        EngineMapping.ToSummary(holiday).Start.Should().Be(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void An_event_without_a_join_link_has_none_and_one_with_a_link_has_one()
    {
        EngineMapping.ToSummary(Meeting("a", false)).HasJoinLink.Should().BeFalse();
        EngineMapping.ToSummary(Meeting("b", true)).HasJoinLink.Should().BeTrue();
    }

    [Fact]
    public void A_takeover_request_without_a_link_has_a_null_join_url()
    {
        var request = EngineMapping.ToRequest(new Pkg.TakeoverRequest { Event = Meeting("n", false), HasStarted = true });
        request.JoinUrl.Should().BeNull();
        request.HasStarted.Should().BeTrue();
        request.SnoozeOptions.Should().BeEmpty();
    }

    // ---- the agenda, problems and lead time (slice 2) -----------------------------------------------------------

    private static Pkg.CalendarEvent Timed(string id, string title, DateTimeOffset start, int minutes = 30, string calendarKey = "google-1/work") => new()
    {
        Id = id,
        Title = title,
        Start = start,
        End = start.AddMinutes(minutes),
        CalendarKey = calendarKey,
        TimeZone = "UTC",
    };

    private static Pkg.EngineState WithAgenda(IEnumerable<(Pkg.CalendarEvent Event, string Phase)> items, string? next = null) => new()
    {
        Events = items.Select(i => i.Event).ToList(),
        Agenda = new Pkg.Agenda { Items = items.Select(i => new Pkg.AgendaItem { EventID = i.Event.Id, State = i.Phase }).ToList(), NextEventID = next },
        Calendars =
        [
            new Pkg.CalendarInfo { Key = "google-1/work", SourceID = "google-1", Title = "Work", ColorHex = "#3366CC" },
            new Pkg.CalendarInfo { Key = "google-1/home", SourceID = "google-1", Title = "Personal" },
        ],
    };

    [Fact]
    public void A_new_AppState_has_an_empty_agenda_no_problems_and_no_lead_time()
    {
        var state = EngineMapping.ToAppState(new Pkg.EngineState());

        state.Agenda.Should().BeEmpty();
        state.Problems.Should().BeEmpty();
        state.LeadTime.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Each_agenda_item_maps_in_the_engines_order_with_its_phase()
    {
        var state = WithAgenda(
        [
            (Timed("a", "Past one", Start), "past"),
            (Timed("b", "Now one", Start.AddHours(1)), "current"),
            (Timed("c", "Later one", Start.AddHours(2)), "upcoming"),
        ]);

        var agenda = EngineMapping.ToAppState(state).Agenda;

        agenda.Select(i => i.EventId).Should().Equal("a", "b", "c");
        agenda.Select(i => i.Phase).Should().Equal(AgendaPhase.Past, AgendaPhase.Current, AgendaPhase.Upcoming);
        agenda[1].Title.Should().Be("Now one");
        agenda[1].Start.Should().Be(Start.AddHours(1));
        agenda[1].End.Should().Be(Start.AddHours(1).AddMinutes(30));
        agenda[1].IsAllDay.Should().BeFalse();
    }

    [Fact]
    public void The_phase_is_the_engines_word_and_never_worked_out_from_the_times()
    {
        // The event is long over by the times, but the engine says it is upcoming: the mapping repeats the engine.
        var state = WithAgenda([(Timed("a", "Odd", new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero)), "upcoming")]);

        EngineMapping.ToAppState(state).Agenda[0].Phase.Should().Be(AgendaPhase.Upcoming);
    }

    [Fact]
    public void A_phase_a_later_engine_adds_shows_as_upcoming()
    {
        var state = WithAgenda([(Timed("a", "Odd", Start), "snoozed")]);

        EngineMapping.ToAppState(state).Agenda[0].Phase.Should().Be(AgendaPhase.Upcoming);
    }

    [Fact]
    public void An_agenda_item_whose_event_is_not_in_the_event_list_is_left_out()
    {
        var state = WithAgenda([(Timed("a", "Here", Start), "upcoming")]) with
        {
            Agenda = new Pkg.Agenda { Items = [new Pkg.AgendaItem { EventID = "ghost", State = "upcoming" }, new Pkg.AgendaItem { EventID = "a", State = "upcoming" }] },
        };

        EngineMapping.ToAppState(state).Agenda.Select(i => i.EventId).Should().Equal("a");
    }

    [Fact]
    public void The_agenda_item_the_engine_names_next_is_flagged()
    {
        var state = WithAgenda([(Timed("a", "A", Start), "past"), (Timed("b", "B", Start.AddHours(1)), "upcoming"), (Timed("c", "C", Start.AddHours(2)), "upcoming")], next: "b");

        EngineMapping.ToAppState(state).Agenda.Select(i => i.IsNext).Should().Equal(false, true, false);
    }

    [Fact]
    public void The_calendar_title_and_colour_come_from_the_events_calendar()
    {
        var state = WithAgenda([(Timed("a", "A", Start), "upcoming"), (Timed("b", "B", Start, calendarKey: "google-1/home"), "upcoming"), (Timed("c", "C", Start, calendarKey: "gone/x"), "upcoming")]);

        var agenda = EngineMapping.ToAppState(state).Agenda;

        (agenda[0].CalendarTitle, agenda[0].CalendarColorHex).Should().Be(("Work", "#3366CC"));
        (agenda[1].CalendarTitle, agenda[1].CalendarColorHex).Should().Be(("Personal", null));
        (agenda[2].CalendarTitle, agenda[2].CalendarColorHex).Should().Be((null, null));
    }

    [Fact]
    public void The_join_url_is_the_first_http_or_https_conference()
    {
        Pkg.CalendarEvent WithLinks(params string[] urls) => Timed("a", "A", Start) with
        {
            Conferences = urls.Select(u => new Pkg.Conference { Url = u, Provider = "other" }).ToList(),
        };

        AgendaOf(WithLinks("https://meet.example/abc")).JoinUrl.Should().Be(new Uri("https://meet.example/abc"));
        AgendaOf(WithLinks("http://meet.example/abc")).JoinUrl.Should().Be(new Uri("http://meet.example/abc"));
        AgendaOf(WithLinks("zoommtg://zoom.us/join?confno=1", "https://zoom.us/j/1")).JoinUrl.Should().Be(new Uri("https://zoom.us/j/1"));
        AgendaOf(WithLinks("zoommtg://zoom.us/join?confno=1")).JoinUrl.Should().BeNull();
        AgendaOf(WithLinks("javascript:alert(1)", "file:///c:/x", "not a url", "")).JoinUrl.Should().BeNull();
        AgendaOf(WithLinks()).JoinUrl.Should().BeNull();
    }

    private static AgendaItem AgendaOf(Pkg.CalendarEvent e) => EngineMapping.ToAppState(WithAgenda([(e, "upcoming")])).Agenda.Single();

    [Fact]
    public void Attendee_count_and_location_are_carried_and_a_blank_location_is_none()
    {
        var item = AgendaOf(Timed("a", "A", Start) with { OtherAttendeeCount = 5, Location = "Room 4" });
        item.OtherAttendeeCount.Should().Be(5);
        item.Location.Should().Be("Room 4");

        AgendaOf(Timed("b", "B", Start) with { Location = "   " }).Location.Should().BeNull();
        AgendaOf(Timed("c", "C", Start)).Location.Should().BeNull();
    }

    [Fact]
    public void An_all_day_agenda_item_keeps_its_calendar_dates()
    {
        var holiday = new Pkg.CalendarEvent
        {
            Id = "h",
            Title = "Holiday",
            IsAllDay = true,
            StartDate = new DateOnly(2026, 3, 29),
            EndDate = new DateOnly(2026, 3, 30),
            TimeZone = "Europe/Berlin",
            CalendarKey = "google-1/home",
        };

        var item = AgendaOf(holiday);

        item.IsAllDay.Should().BeTrue();
        item.StartDate.Should().Be(new DateOnly(2026, 3, 29));
        item.EndDate.Should().Be(new DateOnly(2026, 3, 30));
        item.Start.Date.Should().Be(new DateTime(2026, 3, 29), "the local midnight, in the event's own offset, is still the 29th");
        item.Start.Offset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_multi_day_all_day_event_keeps_both_dates_even_across_a_clock_change()
    {
        // Europe/Berlin changes clocks on 2026-03-29 (02:00 -> 03:00): the end date's midnight has a different offset.
        var trip = new Pkg.CalendarEvent
        {
            Id = "t",
            IsAllDay = true,
            StartDate = new DateOnly(2026, 3, 28),
            EndDate = new DateOnly(2026, 3, 31),
            TimeZone = "Europe/Berlin",
        };

        var item = AgendaOf(trip);

        (item.StartDate, item.EndDate).Should().Be((new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 31)));
        item.End.Offset.Should().Be(TimeSpan.FromHours(2));
    }

    [Fact]
    public void A_timed_event_has_no_calendar_dates()
    {
        var item = AgendaOf(Timed("a", "A", Start));

        item.StartDate.Should().BeNull();
        item.EndDate.Should().BeNull();
    }

    [Fact]
    public void A_merged_event_shows_its_longer_range_but_keeps_the_tug_start()
    {
        var merged = Timed("m", "Sync", Start.AddHours(4)) with { DisplayStart = Start.AddHours(3) };

        var item = AgendaOf(merged);

        item.Start.Should().Be(Start.AddHours(4));
        item.ShownStart.Should().Be(Start.AddHours(3));
        AgendaOf(Timed("p", "Plain", Start)).ShownStart.Should().Be(Start);
    }

    private static Pkg.EventMember Member(string id) => new() { MemberID = id, CalendarKey = "google-1/work", Start = Start, End = Start.AddHours(1), Details = "bare" };

    [Fact]
    public void An_event_with_two_distinct_members_is_merged_and_says_how()
    {
        var merged = Timed("m", "Design review", Start) with
        {
            Members = [Member("design review|1|2"), Member("design review|1|3")],
            MergeProvenance = new Pkg.MergeProvenance { Kind = "inference", EngineName = "Apple Intelligence" },
        };

        var item = AgendaOf(merged);

        item.IsMerged.Should().BeTrue();
        item.MergedEventCount.Should().Be(2);
        item.MergeKind.Should().Be("inference");
        item.MergeEngineName.Should().Be("Apple Intelligence");
    }

    [Fact]
    public void Identical_copies_on_several_calendars_count_as_one_event_and_are_not_a_merge()
    {
        var copies = Timed("m", "Dentist", Start) with { Members = [Member("dentist|1|2"), Member("dentist|1|2"), Member("dentist|1|2")] };

        var item = AgendaOf(copies);

        item.IsMerged.Should().BeFalse();
        item.MergedEventCount.Should().Be(1);
    }

    [Fact]
    public void An_event_with_no_members_is_not_merged()
    {
        var item = AgendaOf(Timed("a", "A", Start));

        item.IsMerged.Should().BeFalse();
        item.MergedEventCount.Should().Be(0);
        item.MergeKind.Should().BeNull();
    }

    private static Pkg.EngineState WithStatuses(IReadOnlyDictionary<string, Pkg.SourceStatus> statuses) => new()
    {
        Statuses = statuses,
        SourceNames = new Dictionary<string, string> { ["google-1"] = "me@example.com", ["caldav-2"] = "Work CalDAV", ["microsoft-3"] = "me@work.example" },
        Accounts =
        [
            new Pkg.Account { KindID = "google", ConnectionID = "1", DisplayName = "me@example.com" },
            new Pkg.Account { KindID = "caldav", ConnectionID = "2", DisplayName = "Work CalDAV" },
            new Pkg.Account { KindID = "microsoft", ConnectionID = "3", DisplayName = "me@work.example" },
        ],
    };

    [Fact]
    public void Each_source_status_maps_to_a_problem_and_ok_maps_to_none()
    {
        var state = WithStatuses(new Dictionary<string, Pkg.SourceStatus>
        {
            ["google-1"] = new() { Kind = "ok" },
            ["caldav-2"] = new() { Kind = "failing", Message = "Server unreachable" },
            ["microsoft-3"] = new() { Kind = "authExpired" },
            ["eventkit"] = new() { Kind = "needsPermission" },
        });

        var problems = EngineMapping.ToAppState(state).Problems;

        problems.Should().Equal(
            new SourceProblem("caldav-2", "Work CalDAV", ProblemKind.Failing, "Server unreachable", "2"),
            new SourceProblem("eventkit", "eventkit", ProblemKind.NeedsPermission, null, null),
            new SourceProblem("microsoft-3", "me@work.example", ProblemKind.AuthExpired, null, "3"));
    }

    [Fact]
    public void All_sources_ok_means_no_problems()
    {
        var state = WithStatuses(new Dictionary<string, Pkg.SourceStatus> { ["google-1"] = new() { Kind = "ok" } });

        EngineMapping.ToAppState(state).Problems.Should().BeEmpty();
    }

    [Fact]
    public void Problems_are_sorted_by_source_id_not_by_dictionary_order()
    {
        var state = WithStatuses(new Dictionary<string, Pkg.SourceStatus>
        {
            ["microsoft-3"] = new() { Kind = "authExpired" },
            ["caldav-2"] = new() { Kind = "failing" },
            ["google-1"] = new() { Kind = "authExpired" },
        });

        EngineMapping.ToAppState(state).Problems.Select(p => p.SourceId).Should().Equal("caldav-2", "google-1", "microsoft-3");
    }

    [Fact]
    public void A_status_kind_a_later_engine_adds_shows_as_failing_with_its_message()
    {
        var state = WithStatuses(new Dictionary<string, Pkg.SourceStatus> { ["google-1"] = new() { Kind = "rateLimited", Message = "Slow down" } });

        EngineMapping.ToAppState(state).Problems.Should().Equal(new SourceProblem("google-1", "me@example.com", ProblemKind.Failing, "Slow down", "1"));
    }

    [Fact]
    public void A_blank_failure_message_is_no_detail()
    {
        var state = WithStatuses(new Dictionary<string, Pkg.SourceStatus> { ["google-1"] = new() { Kind = "failing", Message = "  " } });

        EngineMapping.ToAppState(state).Problems.Single().Detail.Should().BeNull();
    }

    [Fact]
    public void A_source_nobody_owns_keeps_its_id_as_the_name_and_has_no_connection()
    {
        var state = new Pkg.EngineState { Statuses = new Dictionary<string, Pkg.SourceStatus> { ["ms-1"] = new() { Kind = "authExpired" } } };

        EngineMapping.ToAppState(state).Problems.Should().Equal(new SourceProblem("ms-1", "ms-1", ProblemKind.AuthExpired, null, null));
    }

    [Theory]
    [InlineData(60.0, 60)]
    [InlineData(0.0, 0)]
    [InlineData(300.0, 300)]
    [InlineData(90.5, 90.5)]
    public void Lead_time_comes_from_the_engine_settings(double seconds, double expected)
    {
        var state = new Pkg.EngineState { Settings = new Pkg.EngineSettings { Takeover = new Pkg.TakeoverSettings { LeadTime = seconds } } };

        EngineMapping.ToAppState(state).LeadTime.Should().Be(TimeSpan.FromSeconds(expected));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-5.0)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(1e300)]
    public void A_nonsense_lead_time_is_zero_rather_than_a_crash(double seconds)
    {
        var state = new Pkg.EngineState { Settings = new Pkg.EngineSettings { Takeover = new Pkg.TakeoverSettings { LeadTime = seconds } } };

        EngineMapping.ToAppState(state).LeadTime.Should().Be(TimeSpan.Zero);
    }

    // ---- the engine package's own golden state ------------------------------------------------------------------

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private static JsonNode Payload(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", fixture);
        return JsonNode.Parse(File.ReadAllText(path))!["payload"]!;
    }

    private static Pkg.EngineState Read(JsonNode payload) => payload.Deserialize<Pkg.EngineState>(WireOptions)!;

    [Fact]
    public void The_golden_state_maps_to_an_agenda_problems_and_lead_time()
    {
        var state = EngineMapping.ToAppState(Read(Payload("state.json")));

        state.LeadTime.Should().Be(TimeSpan.FromSeconds(120));
        state.Agenda.Select(i => (i.EventId, i.Phase, i.IsNext)).Should().Equal(
            ("google-1/evt-1/1760001800", AgendaPhase.Upcoming, true),
            ("google-1/evt-2/1760007200", AgendaPhase.Upcoming, false));

        var standup = state.Agenda[0];
        standup.Title.Should().Be("Standup");
        standup.CalendarTitle.Should().Be("Work");
        standup.CalendarColorHex.Should().Be("#3366CC");
        standup.JoinUrl.Should().Be(new Uri("https://meet.google.com/abc-defg-hij"));
        standup.OtherAttendeeCount.Should().Be(4);
        standup.IsMerged.Should().BeFalse();

        var review = state.Agenda[1];
        review.IsMerged.Should().BeTrue();
        review.MergedEventCount.Should().Be(2);
        review.MergeEngineName.Should().Be("Apple Intelligence");
        review.ShownStart.Should().Be(new DateTimeOffset(2025, 10, 9, 10, 48, 20, TimeSpan.Zero));
        review.Start.Should().Be(new DateTimeOffset(2025, 10, 9, 10, 53, 20, TimeSpan.Zero));

        // The connection id comes from the library's "<kind>-<connection>" source id rule: caldav-1 is account caldav / 1.
        // The fixture's "ms-1" and "eventkit" sources belong to no account in it.
        state.Problems.Should().Equal(
            new SourceProblem("caldav-1", "Work CalDAV", ProblemKind.Failing, "Server unreachable", "1"),
            new SourceProblem("eventkit", "eventkit", ProblemKind.NeedsPermission, null, null),
            new SourceProblem("ms-1", "ms-1", ProblemKind.AuthExpired, null, null));
    }

    [Fact]
    public void The_golden_all_day_state_keeps_the_dates_in_the_events_zone()
    {
        var payload = Payload("edge-state-all-day-only.json");
        var state = Read(payload);
        state = state with { Agenda = new Pkg.Agenda { Items = [new Pkg.AgendaItem { EventID = state.Events[0].Id, State = "upcoming" }] } };

        var item = EngineMapping.ToAppState(state).Agenda.Single();

        item.IsAllDay.Should().BeTrue();
        (item.StartDate, item.EndDate).Should().Be((new DateOnly(2026, 10, 16), new DateOnly(2026, 10, 17)));
        item.Start.Should().Be(new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void A_field_a_later_engine_adds_anywhere_in_the_state_is_ignored()
    {
        var payload = Payload("state.json");
        payload["futureTopLevel"] = "x";
        payload["agenda"]!["items"]![0]!["futureItemField"] = 1;
        payload["events"]![0]!["futureEventField"] = new JsonObject { ["a"] = 1 };
        payload["statuses"]!["google-1"]!["futureStatusField"] = true;

        var state = EngineMapping.ToAppState(Read(payload));

        state.Agenda.Should().HaveCount(2);
        state.Problems.Should().HaveCount(3);
    }
}
