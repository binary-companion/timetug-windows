using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tug;
using Pkg = TimeTug.Engine;

namespace TimeTug.Presentation.Tests.Startup;

/// <summary>
/// The hold, toast and late-return rules as the running app composes them: engine events and OS-state signals reach the
/// controller through <see cref="TimeTug.Presentation.Startup.AppRuntime"/>, and every request ends with exactly one
/// terminal command.
/// </summary>
public class LateReturnTests
{
    private static readonly TimeSpan IntoTheMeeting = TimeSpan.FromMinutes(11);

    private static async Task<RuntimeRig> StartedAsync(Action<RuntimeRig>? arrange = null)
    {
        var rig = new RuntimeRig();
        arrange?.Invoke(rig);
        await rig.Runtime.StartAsync();
        return rig;
    }

    private static IEnumerable<string> Presented(RuntimeRig rig) => rig.Client.Commands.Where(c => c.StartsWith("presented:", StringComparison.Ordinal));

    private static IEnumerable<string> Closed(RuntimeRig rig) => rig.Client.Commands.Where(c => c.StartsWith("closed:", StringComparison.Ordinal));

    [Fact]
    public async Task Locked_at_fire_time_then_unlocked_ten_minutes_into_a_thirty_minute_meeting_shows_the_full_tug_ten_minutes_late_with_one_presented_report()
    {
        var meeting = Requests.Meeting();
        await using var rig = await StartedAsync(r => r.Session.Lock());
        rig.Client.StateFeed.Push(Requests.StateWith(meeting));
        rig.Client.DueFeed.Push(meeting);
        await rig.Runtime.FlushAsync();

        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.PlansSnapshot.Should().OnlyContain(p => p.Mode == TugMode.None, "nothing is drawn while the session is locked");
        Presented(rig).Should().BeEmpty("a tug nobody saw is not reported as presented");

        rig.Time.SetUtcNow(Requests.Noon + IntoTheMeeting);
        rig.Session.Unlock();
        await rig.Runtime.FlushAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.Mode.Should().Be(TugMode.FullScreen);
        rig.Controller.Plan.LateBy.Should().Be(TimeSpan.FromMinutes(10));
        rig.Controller.Plan.Request.Should().Be(meeting);
        Presented(rig).Should().Equal("presented:e1:screen");
        rig.Client.Terminal.Where(c => !c.StartsWith("presented:", StringComparison.Ordinal)).Should().BeEmpty("the person has not answered yet");
    }

    [Fact]
    public async Task Unlocking_after_the_meeting_ended_closes_the_request_once_and_shows_nothing()
    {
        var meeting = Requests.Meeting();
        await using var rig = await StartedAsync(r => r.Session.Lock());
        rig.Client.StateFeed.Push(Requests.StateWith(meeting));
        rig.Client.DueFeed.Push(meeting);
        await rig.Runtime.FlushAsync();

        rig.Time.SetUtcNow(meeting.End.AddMinutes(5));
        rig.Session.Unlock();
        await rig.Runtime.FlushAsync();

        Closed(rig).Should().Equal("closed:e1");
        rig.PlansSnapshot.Should().OnlyContain(p => p.Mode == TugMode.None, "no tug appears for a meeting that is over");
        Presented(rig).Should().BeEmpty();
        rig.Controller.State.Should().Be(TugState.Hidden);
        rig.Diagnostics.Has("missed, meeting over").Should().BeTrue();
    }

    [Fact]
    public async Task A_meeting_deleted_while_the_session_was_locked_is_closed_once_and_shows_nothing()
    {
        var meeting = Requests.Meeting();
        await using var rig = await StartedAsync(r => r.Session.Lock());
        rig.Client.StateFeed.Push(Requests.StateWith(meeting));
        rig.Client.DueFeed.Push(meeting);
        await rig.Runtime.FlushAsync();

        // The person deletes the meeting from their phone while the PC is locked: the engine's next state no longer lists it.
        rig.Client.StateFeed.Push(Requests.StateWith());
        rig.Time.SetUtcNow(Requests.Noon + TimeSpan.FromMinutes(5));
        rig.Session.Unlock();
        await rig.Runtime.FlushAsync();

        Closed(rig).Should().Equal("closed:e1");
        rig.PlansSnapshot.Should().OnlyContain(p => p.Mode == TugMode.None);
        rig.Diagnostics.Has("no longer on the calendar").Should().BeTrue();
        rig.Diagnostics.Entries.Should().NotContain(e => e.Message.Contains("Design review", StringComparison.Ordinal), "diagnostics never carry the title");
    }

    [Fact]
    public async Task A_refused_toast_holds_the_request_and_raises_the_attention_flag_then_the_tug_is_shown_on_return()
    {
        var meeting = Requests.Meeting();
        await using var rig = await StartedAsync(r =>
        {
            r.Notifications.CanShowFullScreen = false;
            r.Toasts.Accept = false;
        });
        var attention = new List<bool>();
        rig.Controller.AttentionChanged += () => attention.Add(rig.Controller.NeedsAttention);
        rig.Client.StateFeed.Push(Requests.StateWith(meeting));
        rig.Client.DueFeed.Push(meeting);
        await rig.Runtime.FlushAsync();

        rig.Toasts.Shown.Should().Be(1);
        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Controller.NeedsAttention.Should().BeTrue();
        Presented(rig).Should().BeEmpty("Windows never accepted the toast");
        rig.Notifications.Watching.Should().BeTrue("the full-screen watch runs while a request is held");

        rig.Time.SetUtcNow(Requests.Noon + TimeSpan.FromMinutes(3));
        rig.Notifications.EndFullScreen();
        await rig.Runtime.FlushAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.NeedsAttention.Should().BeFalse();
        attention.Should().Equal(true, false);
        rig.Controller.Plan.LateBy.Should().Be(TimeSpan.FromMinutes(2));
        Presented(rig).Should().Equal("presented:e1:screen");
    }

    [Fact]
    public async Task Join_with_a_launcher_that_fails_shows_the_link_then_dismiss_sends_exactly_one_terminal_command()
    {
        var meeting = Requests.Meeting();
        await using var rig = await StartedAsync(r => r.Launcher.Succeeds = false);
        rig.Client.StateFeed.Push(Requests.StateWith(meeting));
        rig.Client.DueFeed.Push(meeting);
        await rig.Runtime.FlushAsync();

        await rig.Controller.JoinAsync();

        rig.Controller.Plan.JoinFailedUrl.Should().Be(meeting.JoinUrl);
        rig.Client.Terminal.Where(c => !c.StartsWith("presented:", StringComparison.Ordinal)).Should().BeEmpty("a failed join sends nothing");

        await rig.Controller.DismissAsync();
        await rig.Controller.DismissAsync();

        rig.Client.Terminal.Where(c => !c.StartsWith("presented:", StringComparison.Ordinal)).Should().Equal("dismiss:e1");
        rig.Controller.State.Should().Be(TugState.Hidden);
    }

    [Fact]
    public async Task Quitting_with_a_request_on_screen_sends_closed_last_and_only_once()
    {
        var meeting = Requests.Meeting();
        await using var rig = await StartedAsync();
        rig.Client.DueFeed.Push(meeting);
        await rig.Runtime.FlushAsync();
        rig.Controller.State.Should().Be(TugState.Shown);

        await rig.Runtime.QuitAsync();
        await rig.Runtime.QuitAsync();

        Closed(rig).Should().Equal("closed:e1");
        rig.Client.Commands[^1].Should().Be("closed:e1", "closing the request is the last thing sent");
        rig.Controller.State.Should().Be(TugState.Hidden);
    }

    [Fact]
    public async Task Quitting_with_a_held_request_also_closes_it()
    {
        var meeting = Requests.Meeting();
        await using var rig = await StartedAsync(r => r.Session.Lock());
        rig.Client.DueFeed.Push(meeting);
        await rig.Runtime.FlushAsync();

        await rig.Runtime.QuitAsync();

        Closed(rig).Should().Equal("closed:e1");
    }

    [Fact]
    public async Task Quitting_with_nothing_outstanding_sends_nothing()
    {
        await using var rig = await StartedAsync();

        await rig.Runtime.QuitAsync();

        rig.Client.Commands.Should().Equal("start");
    }

    [Fact]
    public async Task Through_the_real_engine_client_quitting_sends_closed_to_the_engine_before_the_engine_is_disposed()
    {
        var engine = new FakeEngine();
        var client = new EngineClient(engine);
        var session = new FakeSession();
        var notifications = new FakeNotifications();
        var monitors = new FakeMonitors();
        var diagnostics = new RecordingTugDiagnostics();
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        time.SetUtcNow(Requests.Noon);
        using var controller = new TugController(client, session, notifications, monitors, new FakeToasts(), new FakeLauncher(), diagnostics, time);
        var runtime = new AppRuntime(client, controller, session, notifications, monitors, new MapLocalizer(), diagnostics, time);
        await runtime.StartAsync();
        engine.Push(new Pkg.EngineEvent.TakeoverDue(new Pkg.TakeoverRequest
        {
            Event = new Pkg.CalendarEvent { Id = "e9", Title = "Standup", Start = Requests.Noon.AddMinutes(1), End = Requests.Noon.AddMinutes(31) },
            SnoozeOptions = [60, 300],
        }));
        for (var waited = 0; waited < 5000 && controller.State != TugState.Shown; waited += 10)
        {
            await Task.Delay(10);
        }

        controller.State.Should().Be(TugState.Shown);

        await runtime.QuitAsync();
        engine.Disposed.Should().BeFalse("the engine is disposed by the owner after the quit sequence");
        await client.DisposeAsync();

        engine.Commands.OfType<Pkg.EngineCommand.TakeoverClosed>().Should().ContainSingle().Which.EventID.Should().Be("e9");
        engine.Commands[^1].Should().BeOfType<Pkg.EngineCommand.TakeoverClosed>();
        engine.Disposed.Should().BeTrue();
    }
}
