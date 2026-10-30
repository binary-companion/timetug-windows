using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Instance;
using TimeTug.Presentation.JumpList;

namespace TimeTug.Platform.Tests.Instance;

public class AppInstanceServiceTests
{
    private readonly FakeInstanceRegistry _registry = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingDiagnostics _log = new();
    private readonly FakeTug _tug = new();

    private AppInstanceService Launch() => new(_registry, AppInstanceService.MainKey, _time, _log);

    private static (List<AppActivation> Received, Action<AppActivation> Handler) Collector()
    {
        var received = new List<AppActivation>();
        return (received, received.Add);
    }

    // ---- who is first -------------------------------------------------------------------------------------------

    [Fact]
    public void The_first_launch_is_the_primary_and_a_second_one_is_not()
    {
        using var first = Launch();
        using var second = Launch();

        first.IsPrimary.Should().BeTrue();
        second.IsPrimary.Should().BeFalse();
        _registry.Keys.Should().OnlyContain(k => k == "TimeTug.Main");
    }

    [Fact]
    public async Task A_second_launch_redirects_once_and_the_primary_hears_of_it_once()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        using var second = Launch();

        await second.RedirectAsync(AppActivation.Flyout);

        _registry.Redirects.Should().Be(1);
        received.Should().Equal(AppActivation.Flyout);
    }

    [Fact]
    public async Task A_second_launch_never_raises_an_activation_in_itself()
    {
        using var first = Launch();
        using var second = Launch();
        var (received, handler) = Collector();
        second.Activated += handler;
        second.MarkReady(_tug);

        await second.RedirectAsync(AppActivation.Flyout);

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task The_primary_asked_to_redirect_does_nothing()
    {
        using var first = Launch();

        await first.RedirectAsync(AppActivation.Flyout);

        _registry.Redirects.Should().Be(0);
        _log.Entries.Should().Contain(e => e.Message.Contains("nothing to redirect", StringComparison.Ordinal));
    }

    // ---- before startup has finished ----------------------------------------------------------------------------

    [Fact]
    public async Task An_activation_before_startup_has_finished_is_held_and_delivered_once_afterwards()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        using var second = Launch();

        await second.RedirectAsync(AppActivation.Flyout);
        received.Should().BeEmpty("startup has not finished");

        first.MarkReady(_tug);
        received.Should().Equal(AppActivation.Flyout);

        first.MarkReady(_tug);
        received.Should().HaveCount(1, "a second MarkReady delivers nothing more");
    }

    [Fact]
    public async Task Several_activations_before_startup_has_finished_are_held_as_one()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;

        for (var i = 0; i < 3; i++)
        {
            using var other = Launch();
            await other.RedirectAsync(AppActivation.Flyout);
        }

        first.MarkReady(_tug);

        received.Should().ContainSingle();
    }

    [Fact]
    public void Nothing_held_means_nothing_delivered_when_startup_finishes()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;

        first.MarkReady(_tug);

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task A_held_activation_is_dropped_when_the_tug_is_up_by_the_time_startup_finishes()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        using var second = Launch();
        await second.RedirectAsync(AppActivation.Flyout);
        _tug.IsTugShowing = true;

        first.MarkReady(_tug);

        received.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Message.Contains("tug is showing", StringComparison.Ordinal));
    }

    // ---- while a tug shows, and bursts --------------------------------------------------------------------------

    [Fact]
    public async Task An_activation_while_a_tug_shows_is_ignored_and_logged()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        _tug.IsTugShowing = true;
        using var second = Launch();

        await second.RedirectAsync(AppActivation.Flyout);

        received.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Level == "info" && e.Message.Contains("tug is showing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Once_the_tug_is_gone_the_next_launch_opens_the_flyout()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        _tug.IsTugShowing = true;
        using (var during = Launch())
        {
            await during.RedirectAsync(AppActivation.Flyout);
        }

        _tug.IsTugShowing = false;
        using var after = Launch();
        await after.RedirectAsync(AppActivation.Flyout);

        received.Should().ContainSingle();
    }

    [Fact]
    public async Task Rapid_redirects_are_one_activation()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);

        for (var i = 0; i < 4; i++)
        {
            using var other = Launch();
            await other.RedirectAsync(AppActivation.Flyout);
            _time.Advance(TimeSpan.FromMilliseconds(100));
        }

        received.Should().ContainSingle();
        _registry.Redirects.Should().Be(4, "each launch still handed itself over");
    }

    [Fact]
    public async Task A_launch_after_the_burst_is_a_new_activation()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        using (var one = Launch())
        {
            await one.RedirectAsync(AppActivation.Flyout);
        }

        _time.Advance(AppInstanceService.CoalesceWindow);
        using var two = Launch();
        await two.RedirectAsync(AppActivation.Flyout);

        received.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_wall_clock_stepped_back_does_not_swallow_later_launches()
    {
        var time = new SteppingTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        using var first = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log);
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        using (var one = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log))
        {
            await one.RedirectAsync(AppActivation.Flyout);
        }

        // The machine's clock is set back an hour; ten real seconds pass; the person starts the app again.
        time.StepWallClock(TimeSpan.FromHours(-1));
        time.Advance(TimeSpan.FromSeconds(10));
        using var two = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log);
        await two.RedirectAsync(AppActivation.Flyout);

        received.Should().HaveCount(2, "the launch ten seconds later is a new activation whatever the wall clock says");
    }

    [Fact]
    public async Task A_wall_clock_stepped_forward_does_not_split_a_burst()
    {
        var time = new SteppingTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        using var first = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log);
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        using (var one = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log))
        {
            await one.RedirectAsync(AppActivation.Flyout);
        }

        time.StepWallClock(TimeSpan.FromHours(1));
        time.Advance(TimeSpan.FromMilliseconds(100));
        using var two = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log);
        await two.RedirectAsync(AppActivation.Flyout);

        received.Should().HaveCount(1, "two launches 100 ms apart are one burst");
    }

    [Fact]
    public async Task A_handler_that_throws_does_not_break_later_activations()
    {
        using var first = Launch();
        var calls = 0;
        first.Activated += _ =>
        {
            calls++;
            throw new InvalidOperationException("handler bug");
        };
        first.MarkReady(_tug);

        using (var one = Launch())
        {
            await one.RedirectAsync(AppActivation.Flyout);
        }

        _time.Advance(AppInstanceService.CoalesceWindow);
        using var two = Launch();
        await two.RedirectAsync(AppActivation.Flyout);

        calls.Should().Be(2);
        _log.Entries.Should().Contain(e => e.Level == "error" && e.Message.Contains("InvalidOperationException", StringComparison.Ordinal));
    }

    // ---- a redirect that goes wrong -----------------------------------------------------------------------------

    [Fact]
    public async Task A_redirect_that_fails_is_logged_and_does_not_throw()
    {
        using var first = Launch();
        _registry.RedirectFails = new IOException("gone");
        using var second = Launch();

        var redirect = () => second.RedirectAsync(AppActivation.Flyout);

        await redirect.Should().NotThrowAsync();
        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Message.Contains("IOException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redirect_to_a_primary_that_never_answers_gives_up_after_ten_seconds()
    {
        using var first = Launch();
        _registry.RedirectWaitsFor = new TaskCompletionSource().Task;
        using var second = Launch();

        var redirect = second.RedirectAsync(AppActivation.Flyout);
        redirect.IsCompleted.Should().BeFalse();
        _time.Advance(AppInstanceService.RedirectTimeout - TimeSpan.FromMilliseconds(1));
        redirect.IsCompleted.Should().BeFalse();
        _time.Advance(TimeSpan.FromMilliseconds(1));

        await redirect.WaitAsync(TimeSpan.FromSeconds(5));
        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Message.Contains("did not answer", StringComparison.Ordinal));
    }

    // ---- ending -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_disposed_primary_hears_nothing_more_and_the_next_launch_becomes_primary()
    {
        var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        first.Dispose();

        using var next = Launch();

        next.IsPrimary.Should().BeTrue();
        received.Should().BeEmpty();
        await next.RedirectAsync(AppActivation.Flyout);
        first.Dispose();   // twice is fine
    }

    // ---- the unpackaged fallback --------------------------------------------------------------------------------

    [Fact]
    public async Task Without_package_identity_the_mutex_guard_decides_who_is_first()
    {
        var registry = new MutexInstanceRegistry("tests." + Guid.NewGuid().ToString("N"));
        using var first = new AppInstanceService(registry, AppInstanceService.MainKey, _time, _log);
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        using var second = new AppInstanceService(registry, AppInstanceService.MainKey, _time, _log);

        await second.RedirectAsync(AppActivation.Flyout);

        first.IsPrimary.Should().BeTrue();
        second.IsPrimary.Should().BeFalse("the data belongs to the first instance");
        received.Should().BeEmpty("a mutex cannot carry an activation, and the first copy is not touched");
    }

    [Fact]
    public void Releasing_the_mutex_guard_lets_the_next_launch_take_it()
    {
        var registry = new MutexInstanceRegistry("tests." + Guid.NewGuid().ToString("N"));
        new AppInstanceService(registry, AppInstanceService.MainKey, _time, _log).Dispose();

        using var again = new AppInstanceService(registry, AppInstanceService.MainKey, _time, _log);

        again.IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void The_real_registry_is_refused_without_package_identity_and_the_fallback_takes_over()
    {
        // The test process has no package identity, so the Windows App SDK registry cannot be used here.
        var identity = "tests." + Guid.NewGuid().ToString("N");
        var registry = new FallbackInstanceRegistry(new WindowsInstanceRegistry(), new MutexInstanceRegistry(identity), _log);

        using var first = new AppInstanceService(registry, AppInstanceService.MainKey, _time, _log);
        using var second = new AppInstanceService(registry, AppInstanceService.MainKey, _time, _log);

        first.IsPrimary.Should().BeTrue();
        second.IsPrimary.Should().BeFalse();
        _log.Entries.Should().Contain(e => e.Message.Contains("using the mutex guard", StringComparison.Ordinal));
    }

    [Fact]
    public void The_default_registry_without_an_identity_is_the_mutex_guard()
    {
        var registry = AppInstanceService.DefaultRegistry(packageFamilyName: null, _log);

        registry.Should().BeOfType<MutexInstanceRegistry>();
    }

    [Fact]
    public void The_default_registry_with_an_identity_prefers_the_system_registry()
    {
        var registry = AppInstanceService.DefaultRegistry("BinaryCompanion.TimeTug.Dev_8wekyb3d8bbwe", _log);

        registry.Should().BeOfType<FallbackInstanceRegistry>();
    }

    // ---- jump list actions --------------------------------------------------------------------------------------

    [Theory]
    [InlineData(JumpAction.Flyout)]
    [InlineData(JumpAction.JoinNext)]
    [InlineData(JumpAction.TestTug)]
    [InlineData(JumpAction.Settings)]
    public async Task A_jump_action_reaches_the_primary_with_its_action(JumpAction action)
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        using var second = Launch();

        await second.RedirectAsync(AppActivation.ForJump(action));

        received.Should().Equal(AppActivation.ForJump(action));
        received.Single().Kind.Should().Be(ActivationKind.Jump);
        received.Single().Jump.Should().Be(action);
    }

    [Fact]
    public async Task A_jump_action_before_startup_has_finished_is_held_and_delivered_once_afterwards()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        using var second = Launch();

        await second.RedirectAsync(AppActivation.ForJump(JumpAction.JoinNext));
        received.Should().BeEmpty("startup has not finished");

        first.MarkReady(_tug);
        received.Should().Equal(AppActivation.ForJump(JumpAction.JoinNext));

        first.MarkReady(_tug);
        received.Should().HaveCount(1);
    }

    [Fact]
    public void The_primarys_own_launch_arguments_are_held_until_startup_has_finished_like_any_early_activation()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;

        first.AcceptLaunch(AppActivation.ForJump(JumpAction.Settings));
        received.Should().BeEmpty("the engine has not sent its first state");

        first.MarkReady(_tug);
        received.Should().Equal(AppActivation.ForJump(JumpAction.Settings));
        _log.Entries.Should().Contain(e => e.Message.Contains("held", StringComparison.Ordinal));
    }

    [Fact]
    public void A_plain_launch_of_the_primary_asks_for_nothing()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;

        first.AcceptLaunch(AppActivation.Flyout);
        first.MarkReady(_tug);

        received.Should().BeEmpty("the first launch opens no flyout; only a jump action in its arguments asks for something");
    }

    [Fact]
    public async Task A_jump_action_while_a_tug_shows_is_ignored_and_logged_and_the_tug_wins()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        _tug.IsTugShowing = true;
        using var second = Launch();

        await second.RedirectAsync(AppActivation.ForJump(JumpAction.TestTug));

        received.Should().BeEmpty();
        _log.Entries.Should().Contain(e => e.Level == "info" && e.Message.Contains("tug is showing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_same_action_twice_in_a_burst_is_one_activation_but_a_different_action_is_not_swallowed()
    {
        using var first = Launch();
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);

        foreach (var action in new[] { JumpAction.JoinNext, JumpAction.JoinNext, JumpAction.Settings })
        {
            using var other = Launch();
            await other.RedirectAsync(AppActivation.ForJump(action));
            _time.Advance(TimeSpan.FromMilliseconds(50));
        }

        received.Should().Equal(AppActivation.ForJump(JumpAction.JoinNext), AppActivation.ForJump(JumpAction.Settings));
    }

    [Fact]
    public async Task The_same_action_after_the_burst_is_a_new_activation_whatever_the_wall_clock_did()
    {
        var time = new SteppingTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        using var first = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log);
        var (received, handler) = Collector();
        first.Activated += handler;
        first.MarkReady(_tug);
        using (var one = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log))
        {
            await one.RedirectAsync(AppActivation.ForJump(JumpAction.JoinNext));
        }

        time.StepWallClock(TimeSpan.FromHours(-1));
        time.Advance(TimeSpan.FromSeconds(10));
        using var two = new AppInstanceService(_registry, AppInstanceService.MainKey, time, _log);
        await two.RedirectAsync(AppActivation.ForJump(JumpAction.JoinNext));

        received.Should().HaveCount(2);
    }

    [Fact]
    public async Task What_is_logged_for_a_jump_action_is_its_fixed_word_and_nothing_else()
    {
        using var first = Launch();
        first.MarkReady(_tug);
        using var second = Launch();

        await second.RedirectAsync(AppActivation.ForJump(JumpAction.JoinNext));

        _log.Entries.Should().Contain(e => e.Message.Contains("join-next", StringComparison.Ordinal));
    }
}
