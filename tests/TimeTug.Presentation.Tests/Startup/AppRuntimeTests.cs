using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Tests.Startup;

public class AppRuntimeTests
{
    private static readonly Dictionary<string, string> Strings = new() { ["TestTugTitle"] = "Test meeting", ["TestTugCalendar"] = "TimeTug" };

    private static AccountInfo Account() => new("1", "google", "me@example.test", "ok", 3);

    [Fact]
    public async Task The_app_is_listening_to_the_engine_before_the_engine_is_started()
    {
        await using var rig = new RuntimeRig();

        await rig.Runtime.StartAsync();

        rig.Client.SubscribersAtStart.Should().Be((State: 1, Due: 1, Withdrawn: 1), "nothing the engine says first may be missed");
        rig.Client.Commands.Should().Equal("start");
    }

    [Fact]
    public async Task Starting_twice_starts_the_engine_once()
    {
        await using var rig = new RuntimeRig();

        await rig.Runtime.StartAsync();
        await rig.Runtime.StartAsync();

        rig.Client.Commands.Should().Equal("start");
        rig.Client.DueFeed.SubscriberCount.Should().Be(1);
    }

    [Fact]
    public async Task The_welcome_window_is_wanted_when_the_first_state_has_no_accounts()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();
        rig.Runtime.ShouldShowWelcome.IsCompleted.Should().BeFalse("it waits for the engine's first state");

        rig.Client.StateFeed.Push(RuntimeRig.EmptyState());

        (await rig.Runtime.ShouldShowWelcome).Should().BeTrue();
    }

    [Fact]
    public async Task The_welcome_window_is_not_wanted_when_accounts_already_exist()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();

        rig.Client.StateFeed.Push(RuntimeRig.EmptyState() with { Accounts = [Account()] });

        (await rig.Runtime.ShouldShowWelcome).Should().BeFalse();
    }

    [Fact]
    public async Task Only_the_first_state_decides_the_welcome_window()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();

        rig.Client.StateFeed.Push(RuntimeRig.EmptyState() with { Accounts = [Account()] });
        rig.Client.StateFeed.Push(RuntimeRig.EmptyState());

        (await rig.Runtime.ShouldShowWelcome).Should().BeFalse();
    }

    [Fact]
    public async Task A_state_that_was_already_published_is_still_the_first_state()
    {
        await using var rig = new RuntimeRig();
        rig.Client.StateFeed.Push(RuntimeRig.EmptyState());

        await rig.Runtime.StartAsync();

        (await rig.Runtime.ShouldShowWelcome).Should().BeTrue();
    }

    [Fact]
    public async Task An_engine_that_never_produces_a_state_gives_up_with_a_diagnostic_and_no_welcome_window()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();

        rig.Time.Advance(AppRuntime.FirstStateTimeout + TimeSpan.FromSeconds(1));

        (await rig.Runtime.ShouldShowWelcome).Should().BeFalse();
        rig.Diagnostics.Has("no state").Should().BeTrue();
    }

    [Fact]
    public async Task An_engine_that_fails_to_start_throws_to_the_caller_and_no_welcome_window_is_wanted()
    {
        await using var rig = new RuntimeRig();
        rig.Client.StartFailure = new InvalidOperationException("engine failed");

        var start = async () => await rig.Runtime.StartAsync();

        await start.Should().ThrowAsync<InvalidOperationException>();
        (await rig.Runtime.ShouldShowWelcome).Should().BeFalse();
    }

    [Fact]
    public async Task Test_tug_sends_the_sample_title_and_calendar_from_the_apps_own_strings()
    {
        await using var rig = new RuntimeRig(Strings);
        await rig.Runtime.StartAsync();

        await rig.Runtime.TestTugAsync();

        rig.Client.Commands.Should().Contain("testTug:Test meeting|TimeTug");
    }

    [Fact]
    public async Task A_test_tug_the_engine_sends_back_is_shown_like_any_other_takeover()
    {
        await using var rig = new RuntimeRig(Strings);
        await rig.Runtime.StartAsync();
        var sample = Requests.Meeting("test") with { Title = "Test meeting", CalendarTitle = "TimeTug" };

        rig.Client.DueFeed.Push(sample);
        await rig.Runtime.FlushAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Client.Commands.Should().Contain("presented:test:screen");
    }

    [Fact]
    public async Task Events_are_applied_in_the_order_they_arrive()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();
        var meeting = Requests.Meeting();

        // The state that lists the meeting arrives, the takeover fires, and a newer state without it arrives: unlocking later
        // must see the newest state, not an older one.
        rig.Session.Lock();
        rig.Client.StateFeed.Push(Requests.StateWith(meeting));
        rig.Client.DueFeed.Push(meeting);
        rig.Client.StateFeed.Push(Requests.StateWith());
        await rig.Runtime.FlushAsync();
        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Session.Unlock();
        await rig.Runtime.FlushAsync();

        rig.Client.Commands.Where(c => c.StartsWith("closed:", StringComparison.Ordinal)).Should().Equal("closed:e1");
    }

    [Fact]
    public async Task A_monitor_change_while_the_tug_is_shown_redraws_it_for_the_new_layout()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();
        rig.Client.DueFeed.Push(Requests.Meeting());
        await rig.Runtime.FlushAsync();
        rig.Controller.Plan.Monitors.Should().HaveCount(2);

        rig.Monitors.Set(new TimeTug.Presentation.Abstractions.MonitorInfo("m1", 0, 0, 1920, 1080, true));
        rig.Monitors.Raise();
        await rig.Runtime.FlushAsync();

        rig.Controller.Plan.Monitors.Should().ContainSingle().Which.Role.Should().Be(TugMonitorRole.Primary);
    }

    [Fact]
    public async Task A_withdrawal_for_the_request_on_screen_changes_nothing()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();
        rig.Client.DueFeed.Push(Requests.Meeting());
        await rig.Runtime.FlushAsync();

        rig.Client.WithdrawnFeed.Push("e1");
        await rig.Runtime.FlushAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Client.Terminal.Should().BeEmpty();
    }

    [Fact]
    public async Task Unlocking_with_nothing_outstanding_sends_nothing()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();

        rig.Session.Lock();
        rig.Session.Unlock();
        await rig.Runtime.FlushAsync();

        rig.Client.Commands.Should().Equal("start");
    }

    [Fact]
    public async Task After_quitting_nothing_the_engine_or_the_os_says_reaches_the_controller()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.StartAsync();
        await rig.Runtime.QuitAsync();

        rig.Client.DueFeed.Push(Requests.Meeting());
        rig.Session.Unlock();
        rig.Notifications.EndFullScreen();
        rig.Monitors.Raise();

        rig.Controller.State.Should().Be(TugState.Hidden);
        rig.Client.StateFeed.SubscriberCount.Should().Be(0);
        rig.Client.DueFeed.SubscriberCount.Should().Be(0);
        rig.Client.WithdrawnFeed.SubscriberCount.Should().Be(0);
        rig.Client.Commands.Should().Equal("start");
    }

    [Fact]
    public async Task Flushing_a_runtime_that_was_never_started_or_has_quit_returns_at_once()
    {
        await using var rig = new RuntimeRig();
        await rig.Runtime.FlushAsync();
        await rig.Runtime.StartAsync();
        await rig.Runtime.QuitAsync();

        var flush = async () => await rig.Runtime.FlushAsync();

        await flush.Should().NotThrowAsync();
    }
}
