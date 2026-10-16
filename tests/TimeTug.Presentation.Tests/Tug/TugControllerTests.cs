using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.Presentation.Tests.Tug;

/// <summary>Everything a test needs around one <see cref="TugController"/>.</summary>
internal sealed class Rig : IDisposable
{
    public Rig()
    {
        Time.SetUtcNow(Requests.Noon);
        Controller = new TugController(Engine, Session, Notifications, Monitors, Toasts, Launcher, Diagnostics, Time);
        Controller.PlanChanged += plan => Plans.Add(plan);
    }

    public TugEngineClient Engine { get; } = new();

    public FakeSession Session { get; } = new();

    public FakeNotifications Notifications { get; } = new();

    public FakeMonitors Monitors { get; } = new();

    public FakeToasts Toasts { get; } = new();

    public FakeLauncher Launcher { get; } = new();

    public RecordingTugDiagnostics Diagnostics { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public TugController Controller { get; }

    public List<TugPlan> Plans { get; } = [];

    public void Dispose() => Controller.Dispose();
}

public class TugControllerTests
{
    // ---- Rule 1: shown ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Rule1_a_due_takeover_with_the_session_unlocked_is_shown_on_every_monitor_with_controls_on_the_primary()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();

        await rig.Controller.OnTakeoverDueAsync(request);

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Current.Should().Be(request);
        var plan = rig.Controller.Plan;
        plan.Mode.Should().Be(TugMode.FullScreen);
        plan.Request.Should().Be(request);
        plan.Monitors.Select(m => (m.Monitor.Id, m.Role)).Should().Equal(("m1", TugMonitorRole.Primary), ("m2", TugMonitorRole.Mirror));
        plan.ShowClickToRespond.Should().BeFalse();
        plan.LateBy.Should().BeNull();
        rig.Plans.Should().ContainSingle().Which.Should().Be(plan);
        rig.Engine.Commands.Should().Equal("presented:e1");
        rig.Engine.PresentedOnScreen.Should().Equal(true);
        rig.Controller.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Rule1_when_windows_does_not_give_the_keyboard_the_tug_is_shown_without_focus_and_asks_for_a_click()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.ReportFocusAsync(false);

        rig.Controller.State.Should().Be(TugState.ShownWithoutFocus);
        rig.Controller.Plan.ShowClickToRespond.Should().BeTrue();
        rig.Controller.Plan.Mode.Should().Be(TugMode.FullScreen);

        await rig.Controller.ReportFocusAsync(true);

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.ShowClickToRespond.Should().BeFalse();
        rig.Engine.Commands.Should().Equal("presented:e1");
    }

    [Fact]
    public async Task Rule1_a_focus_report_with_nothing_on_screen_does_nothing()
    {
        using var rig = new Rig();
        await rig.Controller.ReportFocusAsync(false);
        rig.Controller.State.Should().Be(TugState.Hidden);
        rig.Plans.Should().BeEmpty();
    }

    // ---- Rule 2: toast ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Rule2_when_full_screen_is_not_allowed_and_the_toast_is_accepted_it_is_a_toast_reported_as_not_on_screen()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        rig.Controller.State.Should().Be(TugState.Toast);
        rig.Controller.Plan.Mode.Should().Be(TugMode.Toast);
        rig.Toasts.Shown.Should().Be(1);
        rig.Engine.Commands.Should().Equal("presented:e1");
        rig.Engine.PresentedOnScreen.Should().Equal(false);
        rig.Controller.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public async Task Rule2_when_windows_refuses_the_toast_the_request_is_held_with_attention_a_diagnostic_and_no_presented_report()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;
        rig.Toasts.Accept = false;
        var attention = 0;
        rig.Controller.AttentionChanged += () => attention++;

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Controller.NeedsAttention.Should().BeTrue();
        attention.Should().Be(1);
        rig.Engine.Commands.Should().BeEmpty();
        rig.Diagnostics.Has("toast").Should().BeTrue();
        rig.Controller.Plan.Mode.Should().Be(TugMode.None);
    }

    [Fact]
    public async Task Rule2_when_notifications_are_off_the_toast_is_not_even_attempted_and_the_request_is_held()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;
        rig.Notifications.ToastsAllowed = false;

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        rig.Toasts.Shown.Should().Be(0);
        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Controller.NeedsAttention.Should().BeTrue();
        rig.Engine.Commands.Should().BeEmpty();
        rig.Diagnostics.Has("notifications").Should().BeTrue();
    }

    [Fact]
    public async Task Rule2_a_toast_service_that_throws_counts_as_a_refusal()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;
        rig.Toasts.Throw = true;

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Controller.NeedsAttention.Should().BeTrue();
        rig.Engine.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule2_after_a_refused_toast_the_full_tug_appears_on_return_and_the_attention_flag_clears()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;
        rig.Toasts.Accept = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Controller.NeedsAttention.Should().BeTrue();
        var request = Requests.Meeting();
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));
        rig.Time.SetUtcNow(request.Start.AddMinutes(2));

        rig.Notifications.EndFullScreen();
        await rig.Controller.OnFullScreenEndedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.NeedsAttention.Should().BeFalse();
        rig.Controller.Plan.LateBy.Should().Be(TimeSpan.FromMinutes(2));
        rig.Engine.Commands.Should().Equal("presented:e1");
        rig.Engine.PresentedOnScreen.Should().Equal(true);
    }

    // ---- Rule 3: locked -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Rule3_a_locked_session_holds_the_request_with_nothing_shown_and_nothing_reported()
    {
        using var rig = new Rig();
        rig.Session.Lock();

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Controller.Plan.Mode.Should().Be(TugMode.None);
        rig.Toasts.Shown.Should().Be(0);
        rig.Engine.Commands.Should().BeEmpty();
        rig.Controller.Current.Should().NotBeNull();
    }

    // ---- Rule 4: return -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Rule4_unlocking_ten_minutes_into_the_meeting_shows_the_full_tug_late_with_exactly_one_presented_report()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));
        rig.Time.SetUtcNow(request.Start.AddMinutes(10));

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.Mode.Should().Be(TugMode.FullScreen);
        rig.Controller.Plan.LateBy.Should().Be(TimeSpan.FromMinutes(10));
        rig.Controller.Plan.Monitors.Should().HaveCount(2);
        rig.Engine.Commands.Should().Equal("presented:e1");
        rig.Engine.PresentedOnScreen.Should().Equal(true);
    }

    [Fact]
    public async Task Rule4_unlocking_before_the_meeting_starts_shows_the_tug_without_a_late_marker()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.LateBy.Should().BeNull();
    }

    [Fact]
    public async Task Rule4_unlocking_after_the_meeting_ended_sends_closed_once_and_logs_missed()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));
        rig.Time.SetUtcNow(request.End);

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.Hidden);
        rig.Controller.Plan.Mode.Should().Be(TugMode.None);
        rig.Engine.Commands.Should().Equal("closed:e1");
        rig.Diagnostics.Has("missed, meeting over").Should().BeTrue();
    }

    [Fact]
    public async Task Rule4_a_meeting_that_ended_and_dropped_out_of_the_state_is_reported_as_over_not_as_deleted()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith());
        rig.Time.SetUtcNow(request.End.AddMinutes(5));

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Engine.Commands.Should().Equal("closed:e1");
        rig.Diagnostics.Has("missed, meeting over").Should().BeTrue();
    }

    [Fact]
    public async Task Rule4_a_meeting_deleted_while_locked_sends_closed_once_and_logs_no_longer_on_the_calendar()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));
        await rig.Controller.OnEngineStateAsync(Requests.StateWith());
        rig.Time.SetUtcNow(request.Start.AddMinutes(5));

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.Hidden);
        rig.Controller.Plan.Mode.Should().Be(TugMode.None);
        rig.Engine.Commands.Should().Equal("closed:e1");
        rig.Diagnostics.Has("no longer on the calendar").Should().BeTrue();
    }

    [Fact]
    public async Task Rule4_a_merged_meeting_with_a_new_id_but_the_same_title_and_times_is_still_on_the_calendar()
    {
        using var rig = new Rig();
        var request = Requests.Meeting("e1");
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request with { EventId = "merged-9" }));
        rig.Time.SetUtcNow(request.Start.AddMinutes(1));

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Engine.Commands.Should().Equal("presented:e1");
    }

    [Fact]
    public async Task Rule4_a_meeting_moved_while_locked_is_closed_so_the_engine_can_re_arm_the_new_time()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request with { Start = request.Start.AddHours(2), End = request.End.AddHours(2) }));
        rig.Time.SetUtcNow(request.Start.AddMinutes(1));

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Engine.Commands.Should().Equal("closed:e1");
    }

    [Fact]
    public async Task Rule4_with_no_engine_state_seen_yet_the_event_is_not_assumed_gone()
    {
        using var rig = new Rig();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Time.SetUtcNow(Requests.Noon.AddMinutes(3));

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
    }

    [Fact]
    public async Task Rule4_a_return_signal_while_the_session_is_still_locked_changes_nothing()
    {
        using var rig = new Rig();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.OnSessionUnlockedAsync();
        await rig.Controller.OnFullScreenEndedAsync();

        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Engine.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule4_unlocking_while_a_full_screen_app_still_covers_the_screen_keeps_the_request_held()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));
        rig.Notifications.CanShowFullScreen = false;

        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Engine.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule4_a_return_signal_with_nothing_outstanding_does_nothing()
    {
        using var rig = new Rig();
        await rig.Controller.OnSessionUnlockedAsync();
        await rig.Controller.OnFullScreenEndedAsync();
        rig.Engine.Commands.Should().BeEmpty();
        rig.Plans.Should().BeEmpty();
    }

    // ---- Rule 5: withdrawn --------------------------------------------------------------------------------------

    [Fact]
    public async Task Rule5_a_withdrawal_with_nothing_outstanding_or_for_another_event_is_ignored()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverWithdrawnAsync("e1");
        rig.Controller.State.Should().Be(TugState.Hidden);

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting("e1"));
        await rig.Controller.OnTakeoverWithdrawnAsync("other");

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Engine.Commands.Should().Equal("presented:e1");
    }

    [Fact]
    public async Task Rule5_a_withdrawal_for_a_shown_request_leaves_the_tug_up_and_sends_nothing()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.OnTakeoverWithdrawnAsync("e1");

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Engine.Commands.Should().Equal("presented:e1");
    }

    [Fact]
    public async Task Rule5_a_withdrawal_for_a_held_request_sends_nothing_until_the_return_when_the_missing_event_closes_it_once()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));

        await rig.Controller.OnTakeoverWithdrawnAsync("e1");
        await rig.Controller.OnEngineStateAsync(Requests.StateWith());

        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
        rig.Engine.Commands.Should().BeEmpty();

        rig.Time.SetUtcNow(request.Start.AddMinutes(1));
        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Engine.Commands.Should().Equal("closed:e1");
        rig.Controller.State.Should().Be(TugState.Hidden);
    }

    // ---- Rule 6: one terminal command --------------------------------------------------------------------------

    [Fact]
    public async Task Rule6_join_opens_the_link_then_tells_the_engine_once()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.JoinAsync();
        await rig.Controller.JoinAsync();

        rig.Launcher.Opened.Should().Equal("https://meet.example.test/abc");
        rig.Engine.Terminal.Should().Equal("join:e1");
        rig.Controller.State.Should().Be(TugState.Hidden);
        rig.Controller.Current.Should().BeNull();
        rig.Controller.Plan.Mode.Should().Be(TugMode.None);
    }

    [Fact]
    public async Task Rule6_when_the_link_cannot_be_opened_the_plan_carries_the_link_and_the_request_stays_with_no_engine_command()
    {
        using var rig = new Rig();
        rig.Launcher.Succeeds = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.JoinAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.JoinFailedUrl.Should().Be("https://meet.example.test/abc");
        rig.Controller.Plan.Mode.Should().Be(TugMode.FullScreen);
        rig.Controller.Plan.Monitors.Should().HaveCount(2);
        rig.Engine.Terminal.Should().BeEmpty();
        rig.Diagnostics.Has("join link").Should().BeTrue();
        rig.Diagnostics.Entries.Should().NotContain(e => e.Message.Contains("meet.example.test", StringComparison.Ordinal));

        await rig.Controller.DismissAsync();

        rig.Engine.Terminal.Should().Equal("dismiss:e1");
        rig.Controller.State.Should().Be(TugState.Hidden);
    }

    [Fact]
    public async Task Rule6_a_failed_join_can_be_retried_and_then_succeeds_once()
    {
        using var rig = new Rig();
        rig.Launcher.Succeeds = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        await rig.Controller.JoinAsync();
        rig.Launcher.Succeeds = true;

        await rig.Controller.JoinAsync();

        rig.Launcher.Opened.Should().HaveCount(2);
        rig.Engine.Terminal.Should().Equal("join:e1");
    }

    [Fact]
    public async Task Rule6_a_launcher_that_throws_counts_as_a_failure()
    {
        using var local = new ThrowingLauncherRig();
        await local.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await local.Controller.JoinAsync();

        local.Controller.Plan.JoinFailedUrl.Should().NotBeNull();
        local.Engine.Terminal.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule6_join_with_no_link_sends_nothing()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting(joinUrl: null));

        await rig.Controller.JoinAsync();

        rig.Launcher.Opened.Should().BeEmpty();
        rig.Engine.Terminal.Should().BeEmpty();
        rig.Controller.State.Should().Be(TugState.Shown);
    }

    [Fact]
    public async Task Rule6_snooze_sends_the_chosen_duration_once_and_hides()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.SnoozeAsync(TimeSpan.FromMinutes(5));
        await rig.Controller.SnoozeAsync(TimeSpan.FromMinutes(5));
        await rig.Controller.DismissAsync();

        rig.Engine.Terminal.Should().Equal("snooze:e1");
        rig.Engine.Snoozes.Should().Equal(TimeSpan.FromMinutes(5));
        rig.Controller.State.Should().Be(TugState.Hidden);
    }

    [Fact]
    public async Task Rule6_dismiss_sends_dismiss_once_and_hides()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.DismissAsync();
        await rig.Controller.DismissAsync();

        rig.Engine.Terminal.Should().Equal("dismiss:e1");
        rig.Plans.Last().Mode.Should().Be(TugMode.None);
    }

    [Fact]
    public async Task Rule6_responses_with_nothing_outstanding_do_nothing()
    {
        using var rig = new Rig();
        await rig.Controller.JoinAsync();
        await rig.Controller.SnoozeAsync(TimeSpan.FromMinutes(1));
        await rig.Controller.DismissAsync();
        rig.Engine.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule6_a_held_request_cannot_be_answered_from_the_tug_but_is_still_closed_on_return()
    {
        using var rig = new Rig();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.DismissAsync();
        await rig.Controller.JoinAsync();

        rig.Engine.Commands.Should().BeEmpty();
        rig.Controller.State.Should().Be(TugState.HeldUntilReturn);
    }

    [Fact]
    public async Task Rule6_toast_actions_answer_only_the_request_they_were_made_for()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting("e2"));

        await rig.Controller.DismissAsync("e1");
        await rig.Controller.SnoozeAsync("e1", TimeSpan.FromMinutes(5));
        await rig.Controller.JoinAsync("e1");
        rig.Engine.Terminal.Should().BeEmpty();

        await rig.Controller.SnoozeAsync("e2", TimeSpan.FromMinutes(10));

        rig.Engine.Terminal.Should().Equal("snooze:e2");
        rig.Controller.State.Should().Be(TugState.Hidden);
    }

    [Fact]
    public async Task Rule6_joining_from_a_toast_opens_the_link_and_joins_once()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.JoinAsync("e1");

        rig.Launcher.Opened.Should().HaveCount(1);
        rig.Engine.Terminal.Should().Equal("join:e1");
    }

    [Fact]
    public async Task Rule6_a_failed_join_from_a_toast_promotes_to_the_full_tug_when_that_is_possible()
    {
        using var rig = new Rig();
        rig.Notifications.CanShowFullScreen = false;
        rig.Launcher.Succeeds = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Notifications.CanShowFullScreen = true;

        await rig.Controller.JoinAsync("e1");

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.Mode.Should().Be(TugMode.FullScreen);
        rig.Controller.Plan.JoinFailedUrl.Should().NotBeNull();
        rig.Engine.Terminal.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule6_a_newer_request_closes_the_older_one_exactly_once_first()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting("e1"));

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting("test"));

        rig.Engine.Commands.Should().Equal("presented:e1", "closed:e1", "presented:test");
        rig.Controller.Current!.EventId.Should().Be("test");
        rig.Controller.State.Should().Be(TugState.Shown);
    }

    [Fact]
    public async Task Rule6_the_same_request_arriving_again_replaces_it_without_a_terminal_command()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting("test"));
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting("test"));

        rig.Engine.Terminal.Should().BeEmpty();
        rig.Controller.State.Should().Be(TugState.Shown);

        await rig.Controller.DismissAsync();
        rig.Engine.Terminal.Should().Equal("dismiss:test");
    }

    [Fact]
    public async Task Rule6_quitting_with_an_outstanding_request_sends_closed_first_and_only_once()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        await rig.Controller.OnApplicationQuittingAsync();
        await rig.Controller.OnApplicationQuittingAsync();

        rig.Engine.Commands.Should().Equal("presented:e1", "closed:e1");
        rig.Controller.State.Should().Be(TugState.Hidden);
    }

    [Fact]
    public async Task Rule6_quitting_closes_a_held_request_too_and_quitting_with_nothing_outstanding_sends_nothing()
    {
        using var rig = new Rig();
        await rig.Controller.OnApplicationQuittingAsync();
        rig.Engine.Commands.Should().BeEmpty();

        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        await rig.Controller.OnApplicationQuittingAsync();

        rig.Engine.Commands.Should().Equal("closed:e1");
    }

    [Fact]
    public async Task Rule6_an_engine_that_fails_to_accept_a_command_is_logged_and_the_tug_still_closes()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Engine.OnCommand = _ => throw new InvalidOperationException("engine gone");

        await rig.Controller.DismissAsync();

        rig.Controller.State.Should().Be(TugState.Hidden);
        rig.Diagnostics.Entries.Should().Contain(e => e.Level == "error" && e.Category == "tug");
    }

    // ---- Rule 7: monitors ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Rule7_unplugging_the_primary_monitor_moves_the_controls_to_the_new_primary()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Monitors.Set(new MonitorInfo("m2", 0, 0, 1920, 1080, true));

        await rig.Controller.OnMonitorsChangedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.Monitors.Should().ContainSingle()
            .Which.Should().Be(new TugMonitorPlan(new MonitorInfo("m2", 0, 0, 1920, 1080, true), TugMonitorRole.Primary));
        rig.Plans.Should().HaveCount(2);
        rig.Engine.Commands.Should().Equal("presented:e1");
    }

    [Fact]
    public async Task Rule7_plugging_in_a_monitor_adds_a_mirror_and_keeps_the_hint_and_the_join_failure()
    {
        using var rig = new Rig();
        rig.Launcher.Succeeds = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        await rig.Controller.ReportFocusAsync(false);
        await rig.Controller.JoinAsync();
        rig.Monitors.Set(
            new MonitorInfo("m1", 0, 0, 1920, 1080, true),
            new MonitorInfo("m2", 1920, 0, 1920, 1080, false),
            new MonitorInfo("m3", 3840, 0, 1280, 720, false));

        await rig.Controller.OnMonitorsChangedAsync();

        var plan = rig.Controller.Plan;
        plan.Monitors.Select(m => m.Role).Should().Equal(TugMonitorRole.Primary, TugMonitorRole.Mirror, TugMonitorRole.Mirror);
        plan.ShowClickToRespond.Should().BeTrue();
        plan.JoinFailedUrl.Should().NotBeNull();
        rig.Controller.State.Should().Be(TugState.ShownWithoutFocus);
    }

    [Fact]
    public async Task Rule7_the_primary_flag_moving_to_another_monitor_moves_the_controls()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Monitors.Set(new MonitorInfo("m1", 0, 0, 1920, 1080, false), new MonitorInfo("m2", 1920, 0, 1920, 1080, true));

        await rig.Controller.OnMonitorsChangedAsync();

        rig.Controller.Plan.Monitors.Select(m => (m.Monitor.Id, m.Role)).Should()
            .Equal(("m1", TugMonitorRole.Mirror), ("m2", TugMonitorRole.Primary));
    }

    [Fact]
    public async Task Rule7_losing_every_monitor_keeps_the_request_and_the_tug_returns_when_a_monitor_does()
    {
        using var rig = new Rig();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Monitors.Set();

        await rig.Controller.OnMonitorsChangedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.Monitors.Should().BeEmpty();
        rig.Controller.Current.Should().NotBeNull();

        rig.Monitors.Set(new MonitorInfo("m9", 0, 0, 800, 600, true));
        await rig.Controller.OnMonitorsChangedAsync();

        rig.Controller.Plan.Monitors.Should().ContainSingle().Which.Role.Should().Be(TugMonitorRole.Primary);
        rig.Engine.Terminal.Should().BeEmpty();
    }

    [Fact]
    public async Task Rule7_a_monitor_change_while_nothing_is_shown_publishes_nothing()
    {
        using var rig = new Rig();
        await rig.Controller.OnMonitorsChangedAsync();

        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        var before = rig.Plans.Count;
        await rig.Controller.OnMonitorsChangedAsync();

        rig.Plans.Should().HaveCount(before);
        rig.Engine.Commands.Should().BeEmpty();
    }

    [Fact]
    public async Task The_full_screen_watch_runs_only_while_a_request_is_held_or_only_a_toast_was_shown()
    {
        using var rig = new Rig();
        rig.Notifications.Watching.Should().BeFalse();

        rig.Notifications.CanShowFullScreen = false;
        rig.Toasts.Accept = false;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Notifications.Watching.Should().BeTrue("held");

        rig.Toasts.Accept = true;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Notifications.Watching.Should().BeTrue("toast");

        rig.Notifications.CanShowFullScreen = true;
        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());
        rig.Notifications.Watching.Should().BeFalse("shown");

        await rig.Controller.DismissAsync();
        rig.Notifications.Watching.Should().BeFalse("hidden");
    }

    [Fact]
    public async Task A_toast_that_was_shown_is_upgraded_to_the_full_tug_when_full_screen_ends_while_the_meeting_is_on()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Notifications.CanShowFullScreen = false;
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));
        rig.Controller.State.Should().Be(TugState.Toast);
        rig.Time.SetUtcNow(request.Start.AddMinutes(4));

        rig.Notifications.EndFullScreen();
        await rig.Controller.OnFullScreenEndedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.LateBy.Should().Be(TimeSpan.FromMinutes(4));
        rig.Engine.PresentedOnScreen.Should().Equal(false, true);
        rig.Engine.Terminal.Should().BeEmpty();
    }

    // ---- misc ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_clock_is_only_read_from_the_injected_time_provider()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Session.Lock();
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith(request));

        // The OS clock is irrelevant: only the fake provider's time decides "meeting over".
        rig.Time.SetUtcNow(request.End.AddSeconds(-1));
        rig.Session.Unlock();
        await rig.Controller.OnSessionUnlockedAsync();

        rig.Controller.State.Should().Be(TugState.Shown);
        rig.Controller.Plan.LateBy.Should().Be(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
    }

    [Fact]
    public async Task Diagnostics_never_contain_the_meeting_title()
    {
        using var rig = new Rig();
        var request = Requests.Meeting();
        rig.Notifications.CanShowFullScreen = false;
        rig.Toasts.Accept = false;
        await rig.Controller.OnTakeoverDueAsync(request);
        await rig.Controller.OnEngineStateAsync(Requests.StateWith());
        rig.Time.SetUtcNow(request.End);
        rig.Notifications.EndFullScreen();
        await rig.Controller.OnFullScreenEndedAsync();

        rig.Diagnostics.Entries.Should().NotBeEmpty();
        rig.Diagnostics.Entries.Should().NotContain(e => e.Message.Contains("Design review", StringComparison.Ordinal));
    }
}

/// <summary>A rig whose launcher throws instead of returning false.</summary>
internal sealed class ThrowingLauncherRig : IDisposable
{
    public ThrowingLauncherRig()
    {
        Time.SetUtcNow(Requests.Noon);
        Controller = new TugController(Engine, new FakeSession(), new FakeNotifications(), new FakeMonitors(), new FakeToasts(), new ThrowingLauncher(), new RecordingTugDiagnostics(), Time);
    }

    public TugEngineClient Engine { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public TugController Controller { get; }

    public void Dispose() => Controller.Dispose();

    private sealed class ThrowingLauncher : IUrlLauncher
    {
        public ValueTask<bool> LaunchAsync(string url) => throw new InvalidOperationException("shell failure");
    }
}
