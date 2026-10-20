using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Instance;

namespace TimeTug.Platform.Tests.Instance;

public class AppInstanceServiceTests
{
    private readonly FakeInstanceRegistry _registry = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingDiagnostics _log = new();
    private readonly FakeTug _tug = new();

    private AppInstanceService Launch() => new(_registry, AppInstanceService.MainKey, _time, _log);

    private static (List<ActivationKind> Received, Action<ActivationKind> Handler) Collector()
    {
        var received = new List<ActivationKind>();
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

        await second.RedirectAsync(ActivationKind.Flyout);

        _registry.Redirects.Should().Be(1);
        received.Should().Equal(ActivationKind.Flyout);
    }

    [Fact]
    public async Task A_second_launch_never_raises_an_activation_in_itself()
    {
        using var first = Launch();
        using var second = Launch();
        var (received, handler) = Collector();
        second.Activated += handler;
        second.MarkReady(_tug);

        await second.RedirectAsync(ActivationKind.Flyout);

        received.Should().BeEmpty();
    }

    [Fact]
    public async Task The_primary_asked_to_redirect_does_nothing()
    {
        using var first = Launch();

        await first.RedirectAsync(ActivationKind.Flyout);

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

        await second.RedirectAsync(ActivationKind.Flyout);
        received.Should().BeEmpty("startup has not finished");

        first.MarkReady(_tug);
        received.Should().Equal(ActivationKind.Flyout);

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
            await other.RedirectAsync(ActivationKind.Flyout);
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
        await second.RedirectAsync(ActivationKind.Flyout);
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

        await second.RedirectAsync(ActivationKind.Flyout);

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
            await during.RedirectAsync(ActivationKind.Flyout);
        }

        _tug.IsTugShowing = false;
        using var after = Launch();
        await after.RedirectAsync(ActivationKind.Flyout);

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
            await other.RedirectAsync(ActivationKind.Flyout);
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
            await one.RedirectAsync(ActivationKind.Flyout);
        }

        _time.Advance(AppInstanceService.CoalesceWindow);
        using var two = Launch();
        await two.RedirectAsync(ActivationKind.Flyout);

        received.Should().HaveCount(2);
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
            await one.RedirectAsync(ActivationKind.Flyout);
        }

        _time.Advance(AppInstanceService.CoalesceWindow);
        using var two = Launch();
        await two.RedirectAsync(ActivationKind.Flyout);

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

        var redirect = () => second.RedirectAsync(ActivationKind.Flyout);

        await redirect.Should().NotThrowAsync();
        _log.Entries.Should().Contain(e => e.Level == "warning" && e.Message.Contains("IOException", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_redirect_to_a_primary_that_never_answers_gives_up_after_ten_seconds()
    {
        using var first = Launch();
        _registry.RedirectWaitsFor = new TaskCompletionSource().Task;
        using var second = Launch();

        var redirect = second.RedirectAsync(ActivationKind.Flyout);
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
        await next.RedirectAsync(ActivationKind.Flyout);
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

        await second.RedirectAsync(ActivationKind.Flyout);

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
}
