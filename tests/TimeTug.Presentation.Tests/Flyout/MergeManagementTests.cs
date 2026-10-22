using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Tests.Tray;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests.Flyout;

/// <summary>How the engine's merged events and pending look-alike pairs reach the flyout, and what the flyout sends back.</summary>
public sealed class MergeManagementTests : IDisposable
{
    /// <summary>Friday 2026-10-16, noon UTC.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private const string Stale = "That meeting has changed. The list is up to date.";

    private readonly FakeEngineClient _engine = new();
    private readonly FakeTimeProvider _time = new();
    private readonly FlyoutViewModel _model;
    private int _changes;

    public MergeManagementTests()
    {
        _time.SetUtcNow(Noon);
        _model = new FlyoutViewModel(_engine, new FakeLauncher(), _time, new FakeMinuteTicker(), new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"));
        _model.Changed += () => _changes++;
    }

    public void Dispose() => _model.Dispose();

    // ---- the engine's state: members, provenance and candidate pairs --------------------------------------------

    private static readonly JsonSerializerOptions WireOptions = new(JsonSerializerDefaults.Web);

    private static Pkg.EngineState GoldenState()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "state.json");
        return JsonNode.Parse(File.ReadAllText(path))!["payload"]!.Deserialize<Pkg.EngineState>(WireOptions)!;
    }

    [Fact]
    public void The_golden_state_gives_the_merged_event_its_members_with_calendars_and_times()
    {
        var review = EngineMapping.ToAppState(GoldenState()).Agenda.Single(i => i.Title == "Design review");

        review.Members.Select(m => m.MemberId).Should().Equal("design review|1760007200|1760010800", "design review|1760006900|1760010500");
        review.Members[0].Calendars.Should().Equal(new MergeCalendar("google-1/primary", "Work", "me@example.com"));
        review.Members[0].Start.Should().Be(new DateTimeOffset(2025, 10, 9, 10, 53, 20, TimeSpan.Zero));
        review.Members[0].End.Should().Be(new DateTimeOffset(2025, 10, 9, 11, 53, 20, TimeSpan.Zero));
        // A calendar the engine does not list is named by the last part of its key, as on the Mac.
        review.Members[1].Calendars.Should().Equal(new MergeCalendar("caldav-1/work", "work", null));
        review.MergeKind.Should().Be("inference");
        review.MergeEngineName.Should().Be("Apple Intelligence");
    }

    [Fact]
    public void The_golden_states_pending_pair_gives_the_event_its_candidate()
    {
        var agenda = EngineMapping.ToAppState(GoldenState()).Agenda;

        var standup = agenda.Single(i => i.Title == "Standup");
        standup.Candidates.Should().ContainSingle().Which.Should().Be(new MergeCandidate(
            "google-1/evt-2/1760007200", "Design review", new DateTimeOffset(2025, 10, 9, 10, 53, 20, TimeSpan.Zero), new DateTimeOffset(2025, 10, 9, 11, 53, 20, TimeSpan.Zero), false, "Work"));
        agenda.Single(i => i.Title == "Design review").Candidates.Should().BeEmpty("the engine lists the pair under one of the two events here");
        standup.Members.Should().BeEmpty("a plain event has no members to separate");
    }

    private static Pkg.CalendarEvent Event(string id, string title, int startMinutes, int minutes = 30, string calendar = "google-1/work") => new()
    {
        Id = id,
        Title = title,
        Start = Noon.AddMinutes(startMinutes),
        End = Noon.AddMinutes(startMinutes + minutes),
        CalendarKey = calendar,
        TimeZone = "UTC",
    };

    private static Pkg.EventMember Member(string id, string calendar, int startMinutes, int minutes = 30) => new()
    {
        MemberID = id,
        CalendarKey = calendar,
        Start = Noon.AddMinutes(startMinutes),
        End = Noon.AddMinutes(startMinutes + minutes),
        Details = "bare",
    };

    private static Pkg.EngineState StateOf(IReadOnlyList<Pkg.CalendarEvent> events, IReadOnlyDictionary<string, IReadOnlyList<string>>? candidates = null) => new()
    {
        Events = events,
        Agenda = new Pkg.Agenda { Items = events.Select(e => new Pkg.AgendaItem { EventID = e.Id, State = "upcoming" }).ToList() },
        Calendars =
        [
            new Pkg.CalendarInfo { Key = "google-1/work", SourceID = "google-1", Title = "Work", AccountName = "Acme" },
            new Pkg.CalendarInfo { Key = "google-1/team", SourceID = "google-1", Title = "Team", AccountName = "Acme" },
            new Pkg.CalendarInfo { Key = "caldav-2/team", SourceID = "caldav-2", Title = "Team", AccountName = "Home" },
        ],
        Candidates = candidates ?? new Dictionary<string, IReadOnlyList<string>>(),
    };

    [Fact]
    public void Identical_copies_are_one_member_listing_every_calendar_that_holds_one()
    {
        var merged = Event("m", "Sync", 60) with
        {
            Members = [Member("sync|1|2", "google-1/work", 60), Member("sync|1|2", "google-1/team", 60), Member("other|1|3", "caldav-2/team", 65)],
        };

        var item = EngineMapping.ToAppState(StateOf([merged])).Agenda.Single();

        item.Members.Should().HaveCount(2);
        item.Members[0].Calendars.Select(c => c.Title).Should().Equal("Work", "Team");
        item.Members[1].Calendars.Should().Equal(new MergeCalendar("caldav-2/team", "Team", "Home"));
        item.Members[1].Start.Should().Be(Noon.AddMinutes(65));
    }

    [Fact]
    public void Identical_copies_alone_are_not_a_merge_and_offer_nothing_to_separate()
    {
        var copies = Event("m", "Dentist", 60) with { Members = [Member("d|1|2", "google-1/work", 60), Member("d|1|2", "google-1/team", 60)] };

        var item = EngineMapping.ToAppState(StateOf([copies])).Agenda.Single();

        item.IsMerged.Should().BeFalse();
        item.Members.Should().BeEmpty();
    }

    [Fact]
    public void Candidates_name_the_other_event_and_skip_ids_the_engine_no_longer_lists()
    {
        var a = Event("a", "Budget sync", 60);
        var b = Event("b", "Q3 budget", 75, calendar: "google-1/team");
        var state = StateOf([a, b], new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["b", "gone"], ["b"] = ["a"] });

        var agenda = EngineMapping.ToAppState(state).Agenda;

        agenda[0].Candidates.Should().Equal(new MergeCandidate("b", "Q3 budget", Noon.AddMinutes(75), Noon.AddMinutes(105), false, "Team"));
        agenda[1].Candidates.Should().Equal(new MergeCandidate("a", "Budget sync", Noon.AddMinutes(60), Noon.AddMinutes(90), false, "Work"));
    }

    [Fact]
    public void An_all_day_candidate_is_marked_as_all_day()
    {
        var a = Event("a", "Budget sync", 60);
        var holiday = new Pkg.CalendarEvent { Id = "h", Title = "Holiday", IsAllDay = true, StartDate = new DateOnly(2026, 10, 16), EndDate = new DateOnly(2026, 10, 17), TimeZone = "UTC", CalendarKey = "google-1/work" };

        var state = StateOf([a, holiday], new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["h"] });

        EngineMapping.ToAppState(state).Agenda[0].Candidates.Single().IsAllDay.Should().BeTrue();
    }

    [Fact]
    public void An_event_without_candidates_or_members_carries_neither()
    {
        var item = EngineMapping.ToAppState(StateOf([Event("a", "A", 60)])).Agenda.Single();

        item.Members.Should().BeEmpty();
        item.Candidates.Should().BeEmpty();
    }

    // ---- the badge ----------------------------------------------------------------------------------------------

    private static AgendaItem Item(string id, string title, int startMinutes, string calendar = "Work") =>
        new(id, title, Noon.AddMinutes(startMinutes), Noon.AddMinutes(startMinutes + 30), false, AgendaPhase.Upcoming, calendar, "#3366CC", null, 0, false, null);

    private static MergeMember Member(string id, int startMinutes, params (string Title, string? Account)[] calendars) =>
        new(id, Noon.AddMinutes(startMinutes), Noon.AddMinutes(startMinutes + 30), new ValueList<MergeCalendar>(calendars.Select(c => new MergeCalendar("k/" + c.Title, c.Title, c.Account))));

    private static AgendaItem Merged(string id = "m", string kind = "rule", string? engine = null, params MergeMember[] members) =>
        Item(id, "Design review", 60) with
        {
            IsMerged = true,
            MergedEventCount = members.Length,
            MergeKind = kind,
            MergeEngineName = engine,
            Members = new ValueList<MergeMember>(members),
        };

    private static AppState State(params AgendaItem[] agenda) =>
        States.Empty() with { Agenda = agenda, LeadTime = TimeSpan.FromSeconds(60) };

    private static readonly MergeMember Work = Member("design review|1|2", 60, ("Work", null));

    private static readonly MergeMember Team = Member("design review|1|3", 65, ("Team", null));

    [Theory]
    [InlineData("inference", "Apple Intelligence", "Merged with Apple Intelligence", "Merged with Apple Intelligence · 2 events")]
    [InlineData("userConfirmed", null, "Merged manually", "Merged manually · 2 events")]
    [InlineData("rule", null, null, "2 events merged")]
    [InlineData("inference", null, null, "2 events merged")]
    public void The_badge_says_who_merged_the_events_and_a_rule_merge_is_silent(string kind, string? engine, string? badge, string summary)
    {
        _engine.Push(State(Merged(kind: kind, engine: engine, members: [Work, Team])));

        var merge = _model.Rows.Single().Merge!;

        merge.BadgeText.Should().Be(badge);
        merge.Summary.Should().Be(summary);
        _model.Rows.Single().MergeSummary.Should().Be(summary, "the card's line is the same text");
    }

    [Fact]
    public void The_badge_lists_the_calendars_the_copies_are_on_without_repeating_one()
    {
        var both = Member("design review|1|4", 70, ("Team", "Home"), ("Work", null));
        _engine.Push(State(Merged(members: [Work, Team, both])));

        _model.Rows.Single().Merge!.CalendarsText.Should().Be("Work, Team");
    }

    [Fact]
    public void A_plain_event_has_no_merge_info()
    {
        _engine.Push(State(Item("a", "A", 60)));

        _model.Rows.Single().Merge.Should().BeNull();
        _model.Rows.Single().HasMergeActions.Should().BeFalse();
    }

    [Fact]
    public void The_not_the_same_meeting_menu_lists_each_member_by_calendar_and_time()
    {
        var teamAcct = Member("design review|1|3", 65, ("Team", "Home"));
        var both = Member("design review|1|4", 90, ("Work", "Acme"), ("Team", "Acme"));
        _engine.Push(State(Merged(members: [Work, teamAcct, both])));

        var members = _model.Rows.Single().Merge!.Members;

        members.Select(m => m.MemberId).Should().Equal("design review|1|2", "design review|1|3", "design review|1|4");
        members.Select(m => m.Label).Should().Equal(
            "Work · 1:00 – 1:30 PM",
            "Team · 1:05 – 1:35 PM",
            "Work, Team · 1:30 – 2:00 PM");
        members[0].AutomationName.Should().Be("Not the same meeting: Work, 1:00 to 1:30 PM");
        _model.Rows.Single().Merge!.CanSeparate.Should().BeTrue();
    }

    [Fact]
    public void Two_calendars_with_the_same_title_are_told_apart_by_their_account()
    {
        var acme = new MergeMember("sync|1|2", Noon.AddMinutes(60), Noon.AddMinutes(90), [new MergeCalendar("a/cal", "Calendar", "Acme")]);
        var home = new MergeMember("sync|1|3", Noon.AddMinutes(60), Noon.AddMinutes(90), [new MergeCalendar("b/cal", "Calendar", "Home"), new MergeCalendar("b/other", "Other", "Home")]);
        _engine.Push(State(Merged(members: [acme, home])));

        var merge = _model.Rows.Single().Merge!;

        merge.Members.Select(m => m.Label).Should().Equal("Calendar · Acme · 1:00 – 1:30 PM", "Calendar · Home, Other · 1:00 – 1:30 PM");
        merge.CalendarsText.Should().Be("Calendar · Acme, Home, Other");
    }

    [Fact]
    public void A_merge_the_engine_did_not_describe_member_by_member_still_offers_unmerge_only()
    {
        _engine.Push(State(Item("m", "Sync", 60) with { IsMerged = true, MergedEventCount = 2, MergeKind = "rule" }));

        var row = _model.Rows.Single();

        row.Merge!.CanSeparate.Should().BeFalse();
        row.HasMergeActions.Should().BeTrue();
    }

    [Fact]
    public void Candidates_are_listed_as_the_Mac_words_them_with_the_clock_time()
    {
        var candidate = new MergeCandidate("b", "Q3 budget", Noon.AddMinutes(135), Noon.AddMinutes(165), false, "Team");
        var holiday = new MergeCandidate("h", "Holiday", Noon, Noon, true, null);
        _engine.Push(State(Item("a", "Budget sync", 120) with { Candidates = [candidate, holiday] }));

        var row = _model.Rows.Single();

        row.Candidates.Select(c => (c.EventId, c.Label)).Should().Equal(("b", "“Q3 budget” (2:15 PM)"), ("h", "“Holiday” (All day)"));
        row.Candidates[0].AutomationName.Should().Be("Merge with Q3 budget, Team calendar, 2:15 PM");
        row.Candidates[1].AutomationName.Should().Be("Merge with Holiday, all day");
        row.HasMergeActions.Should().BeTrue();
        row.Merge.Should().BeNull();
    }

    [Fact]
    public void Rows_with_the_same_merge_data_are_equal_so_a_clock_tick_does_not_redraw_them()
    {
        _engine.Push(State(Merged(members: [Work, Team]) with { Candidates = [new MergeCandidate("b", "B", Noon, Noon, false, null)] }));
        var before = _changes;

        _engine.Push(State(Merged(members: [Work, Team]) with { Candidates = [new MergeCandidate("b", "B", Noon, Noon, false, null)] }));

        _changes.Should().Be(before, "nothing it shows differs");
    }

    // ---- the actions: ids only ----------------------------------------------------------------------------------

    private static AppState Scene() => State(
        Merged("m", "userConfirmed", members: [Work, Team]),
        Item("a", "Budget sync", 120) with { Candidates = [new MergeCandidate("b", "Q3 budget", Noon.AddMinutes(135), Noon.AddMinutes(165), false, "Team")] },
        Item("b", "Q3 budget", 135, "Team"));

    [Fact]
    public async Task Unmerge_sends_dedup_unmerge_with_the_event_id_and_nothing_else()
    {
        _engine.Push(Scene());

        await _model.UnmergeAsync("m");

        _engine.Dedup.Should().ContainSingle().Which.Command.Should().Be("unmerge:m");
        _engine.Dedup[0].Id.Should().NotBeNullOrEmpty("the id is what a refusal comes back under");
        _model.Notice.Should().BeNull();
    }

    [Fact]
    public async Task Not_the_same_meeting_separates_the_picked_member_by_its_id()
    {
        _engine.Push(Scene());

        await _model.SeparateAsync("m", "design review|1|3");

        _engine.Dedup.Should().ContainSingle().Which.Command.Should().Be("separate:m:design review|1|3");
        _model.Notice.Should().BeNull();
    }

    [Fact]
    public async Task Several_members_can_be_separated_in_one_command()
    {
        _engine.Push(Scene());

        await _model.SeparateAsync("m", ["design review|1|2", "design review|1|3"]);

        _engine.Dedup.Should().ContainSingle().Which.Command.Should().Be("separate:m:design review|1|2,design review|1|3");
    }

    [Fact]
    public async Task Merge_with_sends_dedup_merge_with_the_two_event_ids()
    {
        _engine.Push(Scene());

        await _model.MergeAsync("a", "b");

        _engine.Dedup.Should().ContainSingle().Which.Command.Should().Be("merge:a+b");
        _model.Notice.Should().BeNull();
    }

    [Fact]
    public async Task Every_command_carries_its_own_id()
    {
        _engine.Push(Scene());

        await _model.UnmergeAsync("m");
        await _model.MergeAsync("a", "b");

        _engine.Dedup.Select(d => d.Id).Should().OnlyHaveUniqueItems().And.NotContainNulls();
    }

    // ---- a stale card -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("unmerge")]
    [InlineData("separate")]
    [InlineData("merge")]
    public async Task An_action_on_an_event_that_left_the_list_says_so_sends_nothing_and_does_not_throw(string action)
    {
        _engine.Push(Scene());
        _engine.Push(State(Item("other", "Other", 300)));
        var before = _changes;

        Func<Task> act = async () =>
        {
            switch (action)
            {
                case "unmerge":
                    await _model.UnmergeAsync("m");
                    break;
                case "separate":
                    await _model.SeparateAsync("m", "design review|1|3");
                    break;
                default:
                    await _model.MergeAsync("a", "b");
                    break;
            }
        };

        await act.Should().NotThrowAsync();
        _engine.Dedup.Should().BeEmpty();
        _model.Notice.Should().Be(Stale);
        _changes.Should().BeGreaterThan(before, "the list is refreshed from the latest state and the message appears");
    }

    [Fact]
    public async Task An_unmerge_of_a_card_that_is_no_longer_a_merge_is_stale()
    {
        _engine.Push(Scene());
        _engine.Push(State(Item("m", "Design review", 60), Item("a", "Budget sync", 120)));

        await _model.UnmergeAsync("m");

        _engine.Dedup.Should().BeEmpty();
        _model.Notice.Should().Be(Stale);
    }

    [Fact]
    public async Task A_member_that_is_no_longer_in_the_merge_is_stale()
    {
        _engine.Push(Scene());

        await _model.SeparateAsync("m", "design review|1|99");

        _engine.Dedup.Should().BeEmpty();
        _model.Notice.Should().Be(Stale);
    }

    [Fact]
    public async Task A_candidate_that_is_no_longer_offered_is_stale()
    {
        _engine.Push(Scene());

        await _model.MergeAsync("a", "zzz");

        _engine.Dedup.Should().BeEmpty();
        _model.Notice.Should().Be(Stale);
    }

    [Fact]
    public async Task An_action_before_the_engine_has_spoken_is_stale_rather_than_a_crash()
    {
        await _model.UnmergeAsync("m");

        _engine.Dedup.Should().BeEmpty();
        _model.Notice.Should().Be(Stale);
    }

    // ---- the engine refuses -------------------------------------------------------------------------------------

    [Fact]
    public async Task A_refusal_from_the_engine_shows_the_same_message()
    {
        _engine.Push(Scene());
        await _model.UnmergeAsync("m");
        var id = _engine.Dedup[0].Id!;
        var before = _changes;

        _engine.Push(new EngineDiagnosticInfo("warning", "command", "unknown event id") { CommandId = id });

        _model.Notice.Should().Be(Stale);
        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task A_diagnostic_for_another_command_or_none_is_not_a_refusal()
    {
        _engine.Push(Scene());
        await _model.UnmergeAsync("m");

        _engine.Push(new EngineDiagnosticInfo("warning", "command", "unknown event id") { CommandId = "settings-1" });
        _engine.Push(new EngineDiagnosticInfo("info", "http", "retry"));

        _model.Notice.Should().BeNull();
    }

    [Fact]
    public async Task A_refusal_is_reported_once()
    {
        _engine.Push(Scene());
        await _model.UnmergeAsync("m");
        var id = _engine.Dedup[0].Id!;
        _engine.Push(new EngineDiagnosticInfo("warning", "command", "no") { CommandId = id });
        _time.Advance(TimeSpan.FromSeconds(30));
        (await EventuallyAsync(() => _model.Notice is null)).Should().BeTrue();

        _engine.Push(new EngineDiagnosticInfo("warning", "command", "no") { CommandId = id });

        _model.Notice.Should().BeNull("that command was answered already");
    }

    [Fact]
    public async Task An_engine_that_cannot_be_reached_is_reported_without_throwing()
    {
        _engine.Push(Scene());
        _engine.OnDedup = (_, _) => throw new InvalidOperationException("the engine is gone");

        Func<Task> act = async () => await _model.UnmergeAsync("m");

        await act.Should().NotThrowAsync();
        _model.Notice.Should().Be("Couldn’t change that. Try again.");
    }

    /// <summary>The timer's continuation runs on the thread pool: wait for it to take effect.</summary>
    private static async Task<bool> EventuallyAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
        }

        return condition();
    }

    // ---- the message goes away ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_message_clears_itself_after_a_few_seconds()
    {
        _engine.Push(Scene());
        await _model.MergeAsync("a", "zzz");
        _model.Notice.Should().Be(Stale);
        var before = _changes;

        _time.Advance(TimeSpan.FromSeconds(7));
        await Task.Delay(50);
        _model.Notice.Should().Be(Stale);
        _time.Advance(TimeSpan.FromSeconds(2));

        (await EventuallyAsync(() => _model.Notice is null)).Should().BeTrue();
        _changes.Should().BeGreaterThan(before);
    }

    [Fact]
    public async Task A_new_action_clears_the_old_message_and_an_old_timer_does_not_clear_the_new_one()
    {
        _engine.Push(Scene());
        await _model.MergeAsync("a", "zzz");
        _time.Advance(TimeSpan.FromSeconds(5));

        await _model.UnmergeAsync("m");
        _model.Notice.Should().BeNull();
        await _model.MergeAsync("a", "zzz");
        _time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(50);

        _model.Notice.Should().Be(Stale, "the first message's timer must not clear the second");
    }

    [Fact]
    public async Task Dismissing_the_message_clears_it()
    {
        _engine.Push(Scene());
        await _model.MergeAsync("a", "zzz");

        _model.DismissNotice();

        _model.Notice.Should().BeNull();
    }

    [Fact]
    public async Task After_the_view_model_is_disposed_actions_do_nothing()
    {
        _engine.Push(Scene());
        _model.Dispose();

        await _model.UnmergeAsync("m");

        _engine.Dedup.Should().BeEmpty();
    }
}
