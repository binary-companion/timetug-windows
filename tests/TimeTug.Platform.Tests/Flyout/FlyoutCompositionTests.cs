using Microsoft.Extensions.Time.Testing;
using TimeTug.Platform.Flyout;
using TimeTug.Platform.Hotkey;
using TimeTug.Platform.Instance;
using TimeTug.Platform.Tests.Hotkey;
using TimeTug.Platform.Tests.Instance;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Tests.Flyout;

/// <summary>
/// The composed behaviour of the triggers that open the flyout, with fakes for the machine: the second launch (a real
/// <see cref="AppInstanceService"/> over a fake registry), the global shortcut (a real <see cref="HotkeyService"/> over a fake
/// registrar and store) and the real <see cref="FlyoutHost"/> over a fake window.
/// </summary>
public sealed class FlyoutCompositionTests : IAsyncDisposable
{
    private static readonly HotkeyBinding Chord = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T');

    private readonly FakeInstanceRegistry _registry = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly RecordingDiagnostics _log = new();
    private readonly FakeTug _tug = new();
    private readonly FakeRegistrar _registrar = new();
    private readonly FakeStore _store = new();
    private readonly CountingFactory _windows = new();
    private readonly AppInstanceService _primary;
    private readonly HotkeyService _hotkey;
    private readonly FlyoutHost _host;
    private readonly FlyoutComposition _composition;

    public FlyoutCompositionTests()
    {
        _store.Current = new HotkeyState(Chord, HotkeyStatus.Registered, _time.GetUtcNow());
        var ui = new ImmediateDispatcher();
        _primary = new AppInstanceService(_registry, AppInstanceService.MainKey, _time, _log);
        _hotkey = new HotkeyService(_registrar, _store, _tug, ui, _time, _log);
        var monitors = new OneMonitor();
        _host = new FlyoutHost(_windows, _tug, new QuietSettings(), new NoAnchor(), monitors, ui, _time, _log);
        _composition = new FlyoutComposition(_primary, _hotkey, _host, _tug, ui, _log);
    }

    public async ValueTask DisposeAsync()
    {
        _composition.Dispose();
        _host.Dispose();
        _primary.Dispose();
        await _hotkey.DisposeAsync();
    }

    private async Task SecondLaunchAsync()
    {
        using var second = new AppInstanceService(_registry, AppInstanceService.MainKey, _time, _log);
        await second.RedirectAsync(ActivationKind.Flyout);
    }

    // ---- startup order ------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_launch_that_arrives_before_the_first_state_is_held_and_opens_the_flyout_once_the_first_state_is_in()
    {
        _composition.ConnectActivation();

        await SecondLaunchAsync();
        _windows.Created.Should().Be(0, "the app has not finished starting");
        _host.IsOpen.Should().BeFalse();

        await _composition.CompleteStartupAsync();

        _windows.Created.Should().Be(1);
        _host.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task Two_early_launches_open_one_flyout()
    {
        _composition.ConnectActivation();

        await SecondLaunchAsync();
        await SecondLaunchAsync();
        await _composition.CompleteStartupAsync();

        _windows.Created.Should().Be(1);
    }

    [Fact]
    public void The_shortcut_is_not_registered_before_the_first_state()
    {
        _composition.ConnectActivation();

        _registrar.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task The_stored_shortcut_is_registered_after_the_first_state()
    {
        _composition.ConnectActivation();

        await _composition.CompleteStartupAsync();

        _registrar.Calls.Should().Equal("register Ctrl+Alt+T");
    }

    [Fact]
    public async Task A_shortcut_pressed_before_the_first_state_has_nothing_to_open()
    {
        _composition.ConnectActivation();

        _registrar.Press();

        _windows.Created.Should().Be(0);
        await _composition.CompleteStartupAsync();
        _windows.Created.Should().Be(0);
    }

    [Fact]
    public async Task With_no_shortcut_stored_nothing_is_registered_and_startup_still_completes()
    {
        _store.Current = HotkeyState.None;
        _composition.ConnectActivation();

        await _composition.CompleteStartupAsync();

        _registrar.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_shortcut_that_cannot_be_registered_does_not_stop_startup_and_a_held_launch_still_opens()
    {
        _registrar.RegisterThrows = new InvalidOperationException("no window");
        _composition.ConnectActivation();
        await SecondLaunchAsync();

        var startup = () => _composition.CompleteStartupAsync();

        await startup.Should().NotThrowAsync();
        _host.IsOpen.Should().BeTrue();
    }

    // ---- each trigger opens the flyout exactly once ---------------------------------------------------------------

    [Fact]
    public async Task A_second_launch_opens_the_flyout_exactly_once()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();

        await SecondLaunchAsync();

        _windows.Created.Should().Be(1);
        _host.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task A_second_launch_while_the_flyout_is_open_leaves_it_open()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();
        await SecondLaunchAsync();
        _time.Advance(TimeSpan.FromSeconds(5));

        await SecondLaunchAsync();

        _windows.Created.Should().Be(1);
        _host.IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task The_shortcut_opens_the_flyout_exactly_once_and_the_next_press_closes_it()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();

        _registrar.Press();
        _windows.Created.Should().Be(1);
        _host.IsOpen.Should().BeTrue();

        _time.Advance(TimeSpan.FromSeconds(2));
        _registrar.Press();
        _windows.Created.Should().Be(1);
        _host.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task A_second_launch_and_the_shortcut_together_make_one_open_flyout()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();

        await SecondLaunchAsync();
        _time.Advance(TimeSpan.FromSeconds(2));
        _registrar.Press();

        // The shortcut toggles, so it closed what the launch opened; neither trigger ever leaves two windows.
        _windows.Created.Should().Be(1);
        _host.IsOpen.Should().BeFalse();
    }

    // ---- the tug wins -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Neither_trigger_opens_the_flyout_while_the_tug_shows()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();
        _tug.IsTugShowing = true;

        await SecondLaunchAsync();
        _registrar.Press();

        _windows.Created.Should().Be(0);
        _host.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task A_tug_that_appears_closes_an_open_flyout()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();
        await SecondLaunchAsync();
        _host.IsOpen.Should().BeTrue();

        _tug.IsTugShowing = true;

        _host.IsOpen.Should().BeFalse();
        _windows.Last!.Closes.Should().Be(1);
    }

    // ---- quitting -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Retiring_closes_the_flyout_and_no_trigger_opens_it_again()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();
        await SecondLaunchAsync();
        _host.IsOpen.Should().BeTrue();

        _composition.Retire();
        _time.Advance(TimeSpan.FromSeconds(5));
        await SecondLaunchAsync();
        _registrar.Press();

        _host.IsOpen.Should().BeFalse();
        _windows.Created.Should().Be(1);
        _windows.Last!.Closes.Should().Be(1);
    }

    [Fact]
    public async Task Retiring_twice_is_harmless()
    {
        _composition.ConnectActivation();
        await _composition.CompleteStartupAsync();

        _composition.Retire();
        var again = () => _composition.Retire();

        again.Should().NotThrow();
    }

    [Fact]
    public async Task Without_a_single_instance_service_the_shortcut_still_works()
    {
        var ui = new ImmediateDispatcher();
        using var composition = new FlyoutComposition(null, _hotkey, _host, _tug, ui, _log);
        composition.ConnectActivation();

        await composition.CompleteStartupAsync();
        _registrar.Press();

        _windows.Created.Should().Be(1);
    }

    // ---- fakes --------------------------------------------------------------------------------------------------

    private sealed class CountingFactory : IFlyoutSurfaceFactory
    {
        public int Created { get; private set; }

        public Window? Last { get; private set; }

        public IFlyoutSurface Create()
        {
            Created++;
            return Last = new Window();
        }
    }

    private sealed class Window : IFlyoutSurface
    {
        public int Closes { get; private set; }

        public event Action? Deactivated
        {
            add { }
            remove { }
        }

        public event Action? EscapePressed
        {
            add { }
            remove { }
        }

        public event Action? Closed
        {
            add { }
            remove { }
        }

        public event Action? ContentChanged
        {
            add { }
            remove { }
        }

        public event Action? SettingsRequested
        {
            add { }
            remove { }
        }

        public event Action<string>? AccountsRequested
        {
            add { }
            remove { }
        }

        public SizeInDips MeasureContent() => new(360, 300);

        public void Show(PixelRect bounds, FlyoutLook look)
        {
        }

        public void Update(PixelRect bounds, FlyoutLook look)
        {
        }

        public void Close() => Closes++;
    }

    private sealed class QuietSettings : IUiSettings
    {
        public bool AnimationsEnabled => true;

        public bool TransparencyEffectsEnabled => true;

        public bool HighContrast => false;

        public bool TaskbarUsesLightTheme => false;

        public bool AppsUseDarkTheme => false;

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class NoAnchor : ITrayAnchor
    {
        public AnchorRect? TryGetAnchor() => null;

        public AnchorRect? Peek() => null;

        public void Refresh()
        {
        }

        public void Invalidate()
        {
        }

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    private sealed class OneMonitor : IMonitorService
    {
        public IReadOnlyList<MonitorInfo> Monitors { get; } = [new MonitorInfo("m1", 0, 0, 1920, 1080, true)];

        public MonitorInfo Primary => Monitors[0];

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
