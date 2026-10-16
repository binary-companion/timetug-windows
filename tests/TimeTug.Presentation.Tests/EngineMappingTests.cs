using TimeTug.Presentation.Engine;
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
            new AccountInfo("2", "caldav", "Work CalDAV", "failing", 1),
            new AccountInfo("3", "microsoft", "me@work.example", "authExpired", 0));
    }

    [Fact]
    public void An_account_without_a_known_source_is_ok_with_no_calendars()
    {
        var state = new Pkg.EngineState { Accounts = [new Pkg.Account { KindID = "icloud", ConnectionID = "9", DisplayName = "Home" }] };
        EngineMapping.ToAppState(state).Accounts.Should().Equal(new AccountInfo("9", "icloud", "Home", "ok", 0));
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
}
