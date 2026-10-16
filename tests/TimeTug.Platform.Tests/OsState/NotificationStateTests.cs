using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.OsState;

namespace TimeTug.Platform.Tests.OsState;

public class NotificationStateTests
{
    [Theory]
    [InlineData(UserNotificationState.RunningD3DFullScreen, false, true)]
    [InlineData(UserNotificationState.PresentationMode, false, true)]
    [InlineData(UserNotificationState.Busy, true, true)]
    [InlineData(UserNotificationState.QuietTime, true, false)]
    [InlineData(UserNotificationState.AcceptsNotifications, true, true)]
    [InlineData(UserNotificationState.NotPresent, true, true)]
    [InlineData(UserNotificationState.App, true, true)]
    [InlineData(UserNotificationState.Unknown, true, true)]
    public void Each_windows_state_maps_to_the_two_answers_the_tug_needs(UserNotificationState state, bool fullScreen, bool toasts)
    {
        NotificationState.Classify(state).Should().Be((fullScreen, toasts));
    }

    [Fact]
    public void Toasts_are_allowed_only_when_windows_state_and_the_app_setting_both_allow_them()
    {
        var state = UserNotificationState.AcceptsNotifications;
        var enabled = true;
        using var notifications = new NotificationState(new FakeTimeProvider(), () => state, () => enabled);

        notifications.ToastsAllowed.Should().BeTrue();
        enabled = false;
        notifications.ToastsAllowed.Should().BeFalse("notifications are off for the app");
        enabled = true;
        state = UserNotificationState.QuietTime;
        notifications.ToastsAllowed.Should().BeFalse("quiet time holds toasts");
        state = UserNotificationState.RunningD3DFullScreen;
        notifications.ToastsAllowed.Should().BeTrue("the toast is the fallback when a full-screen app blocks the tug");
        notifications.CanShowFullScreen.Should().BeFalse();
    }

    [Fact]
    public void A_toast_setting_that_cannot_be_read_does_not_block_toasts()
    {
        using var notifications = new NotificationState(new FakeTimeProvider(), () => UserNotificationState.AcceptsNotifications, () => throw new InvalidOperationException("no identity"));
        notifications.ToastsAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task While_watching_the_end_of_full_screen_is_detected_by_polling_and_raised_once()
    {
        var time = new FakeTimeProvider();
        var state = UserNotificationState.RunningD3DFullScreen;
        using var notifications = new NotificationState(time, () => state);
        var ended = 0;
        notifications.FullScreenEnded += () => Interlocked.Increment(ref ended);

        notifications.SetWatchingForFullScreenEnd(true);
        notifications.IsWatching.Should().BeTrue();
        await Tick(time, 3);
        ended.Should().Be(0, "full-screen is still on");

        state = UserNotificationState.AcceptsNotifications;
        await Tick(time, 1);
        await Wait.UntilAsync(() => Volatile.Read(ref ended) == 1, "the end of full-screen");
        await Tick(time, 3);

        ended.Should().Be(1);
    }

    [Fact]
    public async Task Nothing_is_raised_when_full_screen_was_never_on_or_when_watching_has_stopped()
    {
        var time = new FakeTimeProvider();
        var state = UserNotificationState.AcceptsNotifications;
        using var notifications = new NotificationState(time, () => state);
        var ended = 0;
        notifications.FullScreenEnded += () => Interlocked.Increment(ref ended);

        notifications.SetWatchingForFullScreenEnd(true);
        await Tick(time, 3);
        ended.Should().Be(0);

        state = UserNotificationState.RunningD3DFullScreen;
        await Tick(time, 2);
        notifications.SetWatchingForFullScreenEnd(false);
        notifications.IsWatching.Should().BeFalse();
        state = UserNotificationState.AcceptsNotifications;
        await Tick(time, 3);

        ended.Should().Be(0);
    }

    [Fact]
    public void Watching_can_be_switched_on_and_off_repeatedly_and_dispose_stops_it()
    {
        var notifications = new NotificationState(new FakeTimeProvider(), () => UserNotificationState.AcceptsNotifications);
        notifications.SetWatchingForFullScreenEnd(true);
        notifications.SetWatchingForFullScreenEnd(true);
        notifications.IsWatching.Should().BeTrue();
        notifications.SetWatchingForFullScreenEnd(false);
        notifications.SetWatchingForFullScreenEnd(false);
        notifications.IsWatching.Should().BeFalse();
        notifications.SetWatchingForFullScreenEnd(true);

        notifications.Dispose();

        notifications.IsWatching.Should().BeFalse();
        notifications.SetWatchingForFullScreenEnd(true);
        notifications.IsWatching.Should().BeFalse("a disposed service does not restart");
    }

    // The poll loop awaits the timer on another thread; give it a moment to re-arm between ticks.
    private static async Task Tick(FakeTimeProvider time, int count)
    {
        for (var i = 0; i < count; i++)
        {
            await Task.Delay(30);
            time.Advance(NotificationState.DefaultPollInterval);
        }

        await Task.Delay(50);
    }
}
