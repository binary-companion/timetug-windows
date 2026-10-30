using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.JumpList;
using TimeTug.Presentation.Tests.Flyout;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.JumpList;

internal static class Day
{
    public static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static AgendaItem Meeting(
        string id,
        TimeSpan start,
        AgendaPhase phase = AgendaPhase.Upcoming,
        string? join = "https://meet.example.test/room",
        bool next = false,
        bool allDay = false,
        string title = "Planning review",
        string calendar = "Work") =>
        new(id, title, Noon + start, Noon + start + TimeSpan.FromMinutes(30), allDay, phase, calendar, "#3366CC", join is null ? null : new Uri(join), 0, false, null)
        {
            IsNext = next,
        };

    public static AppState State(params AgendaItem[] agenda) => States.Empty() with { Agenda = agenda };
}

public sealed class JumpListPlanTests
{
    [Fact]
    public void Before_the_first_state_and_with_an_empty_day_the_plan_is_the_base_list()
    {
        JumpListPlan.For(null).Should().Be(JumpListPlan.Base);
        JumpListPlan.For(Day.State()).Should().Be(JumpListPlan.Base);
        JumpListPlan.Base.Actions.Should().Equal(JumpAction.Flyout, JumpAction.TestTug, JumpAction.Settings);
    }

    [Fact]
    public void A_next_meeting_with_a_link_adds_join_next_after_the_flyout_task()
    {
        var plan = JumpListPlan.For(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));

        plan.Actions.Should().Equal(JumpAction.Flyout, JumpAction.JoinNext, JumpAction.TestTug, JumpAction.Settings);
    }

    [Fact]
    public void A_meeting_in_progress_with_a_link_adds_it_too()
    {
        var plan = JumpListPlan.For(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(-10), AgendaPhase.Current)));

        plan.Actions.Should().Contain(JumpAction.JoinNext);
    }

    [Fact]
    public void Without_a_link_there_is_no_join_item()
    {
        JumpListPlan.For(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), join: null, next: true))).Should().Be(JumpListPlan.Base);
    }

    [Fact]
    public void A_meeting_that_is_neither_running_nor_next_does_not_count()
    {
        var later = Day.Meeting("later", TimeSpan.FromHours(3));

        JumpListPlan.For(Day.State(later)).Should().Be(JumpListPlan.Base);
    }

    [Fact]
    public void Past_meetings_and_all_day_events_never_count()
    {
        var past = Day.Meeting("past", TimeSpan.FromMinutes(-90), AgendaPhase.Past, next: true);
        var allDay = Day.Meeting("all", TimeSpan.Zero, allDay: true, next: true);

        JumpListPlan.For(Day.State(past, allDay)).Should().Be(JumpListPlan.Base);
    }

    [Fact]
    public void The_meeting_in_progress_is_preferred_and_the_earliest_of_several_wins()
    {
        var next = Day.Meeting("next", TimeSpan.FromMinutes(20), next: true);
        var late = Day.Meeting("running-late", TimeSpan.FromMinutes(-5), AgendaPhase.Current);
        var early = Day.Meeting("running-early", TimeSpan.FromMinutes(-20), AgendaPhase.Current);

        JumpListPlan.JoinEventId(Day.State(next, late, early)).Should().Be("running-early");
        JumpListPlan.JoinEventId(Day.State(next)).Should().Be("next");
    }

    [Fact]
    public void A_running_meeting_without_a_link_falls_back_to_the_next_one_with_a_link()
    {
        var running = Day.Meeting("running", TimeSpan.FromMinutes(-5), AgendaPhase.Current, join: null);
        var next = Day.Meeting("next", TimeSpan.FromMinutes(25), next: true);

        JumpListPlan.JoinEventId(Day.State(running, next)).Should().Be("next");
    }

    [Fact]
    public void The_plan_holds_actions_only_so_two_days_with_the_same_flags_are_equal()
    {
        var one = JumpListPlan.For(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true, title: "One")));
        var two = JumpListPlan.For(Day.State(Day.Meeting("b", TimeSpan.FromMinutes(40), next: true, title: "Two", join: "https://meet.example.test/other")));

        one.Should().Be(two);
    }

    [Theory]
    [InlineData(JumpAction.Flyout, "--jump flyout")]
    [InlineData(JumpAction.JoinNext, "--jump join-next")]
    [InlineData(JumpAction.TestTug, "--jump test-tug")]
    [InlineData(JumpAction.Settings, "--jump settings")]
    public void Each_action_has_its_argument_form(JumpAction action, string expected)
    {
        JumpArguments.For(action).Should().Be(expected);
        JumpArguments.TryParseWord(JumpArguments.Word(action), out var parsed).Should().BeTrue();
        parsed.Should().Be(action);
    }

    [Fact]
    public void An_unknown_word_is_not_an_action()
    {
        JumpArguments.TryParseWord("start", out _).Should().BeFalse();
        JumpArguments.TryParseWord(null, out _).Should().BeFalse();
    }
}

internal sealed class FakeJumpListPlatform : IJumpListPlatform
{
    public bool Supported { get; set; } = true;

    public Exception? SaveFails { get; set; }

    public Exception? SupportFails { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public List<IReadOnlyList<JumpListEntry>> Saves { get; } = [];

    public bool IsSupported => SupportFails is { } failure ? throw failure : Supported;

    public IReadOnlyList<JumpListEntry> Last => Saves.Count == 0 ? [] : Saves[^1];

    public ValueTask<IReadOnlyList<JumpListEntry>> LoadAsync() => ValueTask.FromResult(Last);

    public async ValueTask SaveAsync(IReadOnlyList<JumpListEntry> tasks)
    {
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        if (SaveFails is { } failure)
        {
            throw failure;
        }

        Saves.Add(tasks.ToList());
    }
}

public sealed class JumpListServiceTests
{
    private readonly FakeEngineClient _engine = new();
    private readonly FakeJumpListPlatform _platform = new();
    private readonly ReswLocalizer _text = new();
    private readonly FakeTimeProvider _time = new(Day.Noon);
    private readonly RecordingTugDiagnostics _log = new();

    private JumpListService Service() => new(_engine, _platform, _text, _time, _log);

    private static string[] Names(IReadOnlyList<JumpListEntry> entries) => entries.Select(e => e.DisplayName).ToArray();

    [Fact]
    public async Task An_unsupported_system_logs_once_and_does_nothing()
    {
        _platform.Supported = false;
        var service = Service();

        service.Start();
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(5), next: true)));
        _engine.Push(Day.State());
        await service.StopAsync();

        _platform.Saves.Should().BeEmpty();
        _log.Entries.Count(e => e.Category == "jumplist" && e.Message.Contains("not supported", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void A_platform_that_cannot_even_say_whether_it_is_supported_is_treated_as_unsupported()
    {
        _platform.SupportFails = new InvalidOperationException("no identity");
        var service = Service();

        var start = service.Start;

        start.Should().NotThrow();
        _platform.Saves.Should().BeEmpty();
        _log.Entries.Should().ContainSingle(e => e.Message.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Startup_saves_the_base_list_before_any_state_arrives()
    {
        var service = Service();

        service.Start();
        await service.FlushAsync();

        _platform.Saves.Should().ContainSingle();
        _platform.Last.Select(e => e.Arguments).Should().Equal("--jump flyout", "--jump test-tug", "--jump settings");
        Names(_platform.Last).Should().Equal("Open today's meetings", "Test tug", "Settings");
    }

    [Fact]
    public async Task A_join_link_adds_the_item_and_losing_it_removes_it_again()
    {
        var service = Service();
        service.Start();
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        await service.FlushAsync();

        Names(_platform.Last).Should().Equal("Open today's meetings", "Join next meeting", "Test tug", "Settings");
        _platform.Last.Select(e => e.Arguments).Should().Contain("--jump join-next");

        _engine.Push(Day.State());
        await service.FlushAsync();

        Names(_platform.Last).Should().NotContain("Join next meeting");
    }

    [Fact]
    public async Task The_list_is_saved_again_only_when_the_plan_changes_never_per_state()
    {
        var service = Service();
        service.Start();
        await service.FlushAsync();
        var afterStart = _platform.Saves.Count;

        for (var i = 0; i < 10; i++)
        {
            _engine.Push(Day.State());
        }

        await service.FlushAsync();
        _platform.Saves.Count.Should().Be(afterStart, "the plan is still the base plan");

        for (var i = 0; i < 10; i++)
        {
            _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10 + i), next: true)));
        }

        await service.FlushAsync();
        _platform.Saves.Count.Should().Be(afterStart + 1, "ten states with the same flag are one change");
    }

    [Fact]
    public async Task A_state_that_arrived_before_start_is_picked_up()
    {
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        var service = Service();

        service.Start();
        await service.FlushAsync();

        Names(_platform.Last).Should().Contain("Join next meeting");
    }

    [Fact]
    public async Task A_failing_platform_is_logged_with_its_type_only_and_never_throws_and_the_next_change_tries_again()
    {
        _platform.SaveFails = new IOException("disk full for My Secret Meeting");
        var service = Service();

        var start = service.Start;
        start.Should().NotThrow();
        await service.FlushAsync();

        service.Saved.Should().BeNull();
        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Message.Contains("IOException", StringComparison.Ordinal));
        _log.Entries.Should().NotContain(e => e.Message.Contains("Secret", StringComparison.Ordinal), "an exception's message is never logged");

        _platform.SaveFails = null;
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        await service.FlushAsync();

        service.Saved.Should().Be(JumpListPlan.For(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true))));
        Names(_platform.Last).Should().Contain("Join next meeting");
    }

    [Fact]
    public async Task Quitting_saves_the_base_list_so_no_stale_join_item_is_left_on_disk()
    {
        var service = Service();
        service.Start();
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        await service.FlushAsync();
        Names(_platform.Last).Should().Contain("Join next meeting");

        await service.StopAsync();

        _platform.Last.Select(e => e.Arguments).Should().Equal("--jump flyout", "--jump test-tug", "--jump settings");
        service.Saved.Should().Be(JumpListPlan.Base);
    }

    [Fact]
    public async Task After_quitting_a_late_state_changes_nothing()
    {
        var service = Service();
        service.Start();
        await service.StopAsync();
        var saves = _platform.Saves.Count;

        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        await service.FlushAsync();

        _platform.Saves.Count.Should().Be(saves);
    }

    [Fact]
    public async Task Quitting_while_a_save_is_stuck_gives_up_after_the_timeout()
    {
        var service = Service();
        service.Start();
        await service.FlushAsync();
        _platform.Gate = new TaskCompletionSource();
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));

        var stop = service.StopAsync();
        stop.IsCompleted.Should().BeFalse();
        _time.Advance(JumpListService.StopTimeout + TimeSpan.FromSeconds(1));
        await stop;

        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Message.Contains("took too long", StringComparison.Ordinal));
        _platform.Gate.SetResult();
    }

    [Fact]
    public async Task Saves_never_overlap_and_the_latest_plan_wins()
    {
        var service = Service();
        _platform.Gate = new TaskCompletionSource();
        service.Start();
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        _engine.Push(Day.State());
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));

        _platform.Saves.Should().BeEmpty("the first save is waiting");
        _platform.Gate.SetResult();
        _platform.Gate = null;
        await service.FlushAsync();

        Names(_platform.Last).Should().Contain("Join next meeting");
        service.Saved.Should().Be(JumpListPlan.For(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true))));
    }

    [Fact]
    public async Task Item_names_and_arguments_carry_nothing_from_the_calendar_and_neither_does_the_log()
    {
        var secrets = new[] { "Quarterly layoffs review", "https://meet.example.test/private-room", "Confidential Calendar" };
        AgendaItem Secret(AgendaPhase phase) =>
            new("s", secrets[0], Day.Noon.AddHours(4), Day.Noon.AddHours(4).AddMinutes(30), false, phase, secrets[2], "#AA0000", new Uri(secrets[1]), 3, false, "Room 12 " + secrets[0]) { IsNext = true };

        var service = Service();
        service.Start();
        for (var minute = 0; minute <= 5 * 60; minute += 5)
        {
            _engine.Push(Day.State(Secret(minute >= 240 ? AgendaPhase.Current : AgendaPhase.Upcoming)));
        }

        _engine.Push(Day.State(Secret(AgendaPhase.Past)));
        await service.StopAsync();

        _platform.Saves.Should().HaveCountGreaterThan(2);
        foreach (var entry in _platform.Saves.SelectMany(s => s))
        {
            foreach (var secret in secrets)
            {
                entry.DisplayName.Should().NotContain(secret);
                entry.Arguments.Should().NotContain(secret);
            }

            entry.Arguments.Should().StartWith("--jump ");
            entry.Arguments.Should().NotContain("example.test");
        }

        _log.Entries.Should().NotBeEmpty();
        foreach (var secret in secrets)
        {
            _log.Entries.Should().NotContain(e => e.Message.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }

        _log.Entries.Should().NotContain(e => e.Message.Contains("example.test", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("Room 12", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_names_come_from_the_resources_and_every_one_exists()
    {
        var service = Service();
        service.Start();
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        await service.FlushAsync();

        foreach (var key in new[] { "JumpList_Flyout", "JumpList_JoinNext", "JumpList_TestTug", "JumpList_Settings" })
        {
            _text.TryGetString(key).Should().NotBeNullOrWhiteSpace(key);
            Names(_platform.Last).Should().Contain(_text.GetString(key));
        }
    }

    [Fact]
    public async Task Item_names_are_short_enough_for_a_jump_list()
    {
        var service = Service();
        service.Start();
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        await service.FlushAsync();

        Names(_platform.Last).Should().OnlyContain(n => n.Length > 0 && n.Length <= 40);
    }
}

internal sealed class RecordingJumpActions : IJumpActions
{
    public List<string> Calls { get; } = [];

    public bool JoinFails { get; set; }

    public void ShowFlyout() => Calls.Add("flyout");

    public ValueTask JoinAsync(string eventId)
    {
        Calls.Add("join:" + eventId);
        return JoinFails ? throw new InvalidOperationException("launcher") : ValueTask.CompletedTask;
    }

    public ValueTask TestTugAsync()
    {
        Calls.Add("test-tug");
        return ValueTask.CompletedTask;
    }

    public void OpenSettings() => Calls.Add("settings");
}

public sealed class JumpActionHandlerTests : IDisposable
{
    private readonly FakeEngineClient _engine = new();
    private readonly RecordingJumpActions _actions = new();
    private readonly FakeTugPresence _tug = new();
    private readonly RecordingTugDiagnostics _log = new();
    private readonly JumpActionHandler _handler;

    public JumpActionHandlerTests() => _handler = new JumpActionHandler(_engine, _actions, _tug, _log);

    public void Dispose() => _handler.Dispose();

    [Fact]
    public async Task Flyout_opens_the_flyout_once()
    {
        await _handler.HandleAsync(JumpAction.Flyout);

        _actions.Calls.Should().Equal("flyout");
    }

    [Fact]
    public async Task Test_tug_sends_the_test_tug_once()
    {
        await _handler.HandleAsync(JumpAction.TestTug);

        _actions.Calls.Should().Equal("test-tug");
    }

    [Fact]
    public async Task Settings_opens_settings_once()
    {
        await _handler.HandleAsync(JumpAction.Settings);

        _actions.Calls.Should().Equal("settings");
    }

    [Fact]
    public async Task Join_next_joins_the_meeting_the_engine_names_now()
    {
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));

        await _handler.HandleAsync(JumpAction.JoinNext);

        _actions.Calls.Should().Equal("join:a");
    }

    [Fact]
    public async Task A_stale_join_next_opens_the_flyout_instead_when_the_link_is_gone()
    {
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), join: null, next: true)));

        await _handler.HandleAsync(JumpAction.JoinNext);

        _actions.Calls.Should().Equal("flyout");
        _log.Entries.Should().Contain(e => e.Message.Contains("no meeting to join", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Join_next_before_any_state_opens_the_flyout()
    {
        await _handler.HandleAsync(JumpAction.JoinNext);

        _actions.Calls.Should().Equal("flyout");
    }

    [Theory]
    [InlineData(JumpAction.Flyout)]
    [InlineData(JumpAction.JoinNext)]
    [InlineData(JumpAction.TestTug)]
    [InlineData(JumpAction.Settings)]
    public async Task While_the_tug_shows_every_action_is_ignored_and_logged(JumpAction action)
    {
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        _tug.IsTugShowing = true;

        await _handler.HandleAsync(action);

        _actions.Calls.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Message.Contains("tug is showing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_action_that_throws_is_logged_by_type_and_does_not_escape()
    {
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true)));
        _actions.JoinFails = true;

        var act = async () => await _handler.HandleAsync(JumpAction.JoinNext);

        await act.Should().NotThrowAsync();
        _log.Entries.Should().Contain(e => e.Level == "error" && e.Message.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_log_names_no_meeting()
    {
        _engine.Push(Day.State(Day.Meeting("a", TimeSpan.FromMinutes(10), next: true, title: "Quarterly layoffs review")));

        await _handler.HandleAsync(JumpAction.JoinNext);
        _tug.IsTugShowing = true;
        await _handler.HandleAsync(JumpAction.JoinNext);

        _log.Entries.Should().NotContain(e => e.Message.Contains("layoffs", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("example.test", StringComparison.Ordinal));
    }
}
