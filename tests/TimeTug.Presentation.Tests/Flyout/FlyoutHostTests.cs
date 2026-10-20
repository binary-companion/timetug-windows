using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Flyout;

internal sealed class FakeSurface : IFlyoutSurface
{
    public bool IsShown { get; private set; }

    public int Closes { get; private set; }

    public PixelRect? Bounds { get; private set; }

    public FlyoutLook? Look { get; private set; }

    public int Updates { get; private set; }

    public SizeInDips Content { get; set; } = new(360, 300);

    public bool ThrowOnShow { get; set; }

    public event Action? Deactivated;

    public event Action? EscapePressed;

    public event Action? Closed;

    public event Action? ContentChanged;

    public SizeInDips MeasureContent() => Content;

    public void Show(PixelRect bounds, FlyoutLook look)
    {
        if (ThrowOnShow)
        {
            throw new InvalidOperationException("no window");
        }

        IsShown = true;
        Bounds = bounds;
        Look = look;
    }

    public void Update(PixelRect bounds, FlyoutLook look)
    {
        Updates++;
        Bounds = bounds;
        Look = look;
    }

    public void Close()
    {
        Closes++;
        IsShown = false;
    }

    public void RaiseDeactivated() => Deactivated?.Invoke();

    public void RaiseEscape() => EscapePressed?.Invoke();

    public void RaiseClosed() => Closed?.Invoke();

    public void RaiseContentChanged() => ContentChanged?.Invoke();
}

internal sealed class FakeSurfaceFactory : IFlyoutSurfaceFactory
{
    public List<FakeSurface> Created { get; } = [];

    public FakeSurface? Last => Created.Count == 0 ? null : Created[^1];

    public bool ThrowOnCreate { get; set; }

    public IFlyoutSurface Create()
    {
        if (ThrowOnCreate)
        {
            throw new InvalidOperationException("no windows");
        }

        var surface = new FakeSurface();
        Created.Add(surface);
        return surface;
    }
}

internal sealed class FakeTugPresence : ITugPresence
{
    private bool _showing;

    public bool IsTugShowing
    {
        get => _showing;
        set
        {
            _showing = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}

internal sealed class FakeAnchor : ITrayAnchor
{
    public AnchorRect? Anchor { get; set; } = new(new PixelRect(1700, 1044, 24, 24), AnchorSource.Icon);

    public int Asked { get; private set; }

    public AnchorRect? TryGetAnchor()
    {
        Asked++;
        return Anchor;
    }
}

public sealed class FlyoutHostTests : IDisposable
{
    private readonly FakeSurfaceFactory _factory = new();
    private readonly FakeTugPresence _tug = new();
    private readonly FakeUiSettings _settings = new();
    private readonly FakeAnchor _anchor = new();
    private readonly FakeMonitors _monitors = new();
    private readonly FakeTimeProvider _time = new();
    private readonly RecordingTugDiagnostics _diagnostics = new();
    private readonly FlyoutHost _host;
    private int _openChanges;

    public FlyoutHostTests()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero));
        _monitors.Set(new MonitorInfo("m1", 0, 0, 1920, 1080, true) { WorkArea = new PixelRect(0, 0, 1920, 1032) });
        _host = new FlyoutHost(_factory, _tug, _settings, _anchor, _monitors, new ImmediateDispatcher(), _time, _diagnostics);
        _host.OpenChanged += () => _openChanges++;
    }

    public void Dispose() => _host.Dispose();

    // ---- opening and closing ------------------------------------------------------------------------------------

    [Fact]
    public void Toggle_opens_the_flyout_next_to_the_icon()
    {
        _host.Toggle().Should().BeTrue();

        _host.IsOpen.Should().BeTrue();
        _factory.Created.Should().ContainSingle();
        _factory.Last!.IsShown.Should().BeTrue();
        _factory.Last.Bounds.Should().Be(new PixelRect(1712 - 180, 1032 - 8 - 300, 360, 300), "above the taskbar, centred on the icon");
        _openChanges.Should().Be(1);
    }

    [Fact]
    public void Toggle_again_closes_it()
    {
        _host.Toggle();

        _host.Toggle().Should().BeFalse();

        _host.IsOpen.Should().BeFalse();
        _factory.Last!.Closes.Should().Be(1);
        _openChanges.Should().Be(2);
    }

    [Fact]
    public void Each_opening_makes_a_new_window()
    {
        _host.Toggle();
        _host.Toggle();
        _time.Advance(TimeSpan.FromSeconds(5));
        _host.Toggle();

        _factory.Created.Should().HaveCount(2);
        _factory.Last!.IsShown.Should().BeTrue();
    }

    [Fact]
    public void Esc_closes_it()
    {
        _host.Toggle();

        _factory.Last!.RaiseEscape();

        _host.IsOpen.Should().BeFalse();
        _factory.Last.Closes.Should().Be(1);
    }

    [Fact]
    public void Losing_the_active_state_closes_it()
    {
        _host.Toggle();

        _factory.Last!.RaiseDeactivated();

        _host.IsOpen.Should().BeFalse();
        _factory.Last.Closes.Should().Be(1);
    }

    [Fact]
    public void The_tray_click_that_caused_the_deactivation_does_not_open_it_again()
    {
        _host.Toggle();
        _factory.Last!.RaiseDeactivated();

        _time.Advance(TimeSpan.FromMilliseconds(80));
        _host.Toggle().Should().BeFalse("this is the same click, arriving after the window lost the active state");

        _time.Advance(FlyoutHost.ClickGrace);
        _host.Toggle().Should().BeTrue("a click well after that opens it");
    }

    [Fact]
    public void After_Esc_the_next_click_opens_it_straight_away()
    {
        _host.Toggle();
        _factory.Last!.RaiseEscape();

        _host.Toggle().Should().BeTrue();
    }

    // ---- Show: what a second launch or another trigger that only wants it open calls -----------------------------

    [Fact]
    public void Show_opens_a_closed_flyout()
    {
        _host.Show().Should().BeTrue();

        _host.IsOpen.Should().BeTrue();
        _factory.Created.Should().ContainSingle();
        _openChanges.Should().Be(1);
    }

    [Fact]
    public void Show_leaves_an_open_flyout_as_it_is_where_Toggle_would_close_it()
    {
        _host.Toggle();

        _host.Show().Should().BeTrue();

        _host.IsOpen.Should().BeTrue();
        _factory.Created.Should().ContainSingle("no second window");
        _factory.Last!.Closes.Should().Be(0);
        _openChanges.Should().Be(1);
    }

    [Fact]
    public void Two_Shows_in_a_row_open_one_window()
    {
        _host.Show();
        _host.Show();

        _factory.Created.Should().ContainSingle();
        _openChanges.Should().Be(1);
    }

    [Fact]
    public void Show_is_refused_while_a_tug_is_showing()
    {
        _tug.IsTugShowing = true;

        _host.Show().Should().BeFalse();

        _host.IsOpen.Should().BeFalse();
        _factory.Created.Should().BeEmpty();
        _diagnostics.Has("stays closed while the tug is showing").Should().BeTrue();
    }

    [Fact]
    public void Show_opens_right_after_a_deactivation_because_it_is_not_a_tray_click()
    {
        _host.Toggle();
        _factory.Last!.RaiseDeactivated();

        _host.Show().Should().BeTrue();
    }

    [Fact]
    public void Show_after_the_host_is_disposed_does_nothing()
    {
        _host.Dispose();

        _host.Show().Should().BeFalse();

        _factory.Created.Should().BeEmpty();
    }

    [Fact]
    public void A_window_closed_by_someone_else_is_forgotten()
    {
        _host.Toggle();

        _factory.Last!.RaiseClosed();

        _host.IsOpen.Should().BeFalse();
        _factory.Last.Closes.Should().Be(0, "it is already closed");
        _host.Toggle().Should().BeTrue();
    }

    [Fact]
    public void A_window_that_cannot_be_made_or_shown_is_recorded_and_leaves_the_flyout_closed()
    {
        _factory.ThrowOnCreate = true;
        _host.Toggle().Should().BeFalse();
        _diagnostics.Has("could not be created").Should().BeTrue();

        _factory.ThrowOnCreate = false;
        var failing = new FakeSurface { ThrowOnShow = true };
        var host = new FlyoutHost(new SingleFactory(failing), _tug, _settings, _anchor, _monitors, new ImmediateDispatcher(), _time, _diagnostics);
        host.Toggle().Should().BeFalse();
        host.IsOpen.Should().BeFalse();
        _diagnostics.Has("could not be shown").Should().BeTrue();
        failing.Closes.Should().Be(1);
        host.Dispose();
    }

    // ---- the tug wins -------------------------------------------------------------------------------------------

    [Fact]
    public void Toggle_is_refused_while_a_tug_is_showing()
    {
        _tug.IsTugShowing = true;

        _host.Toggle().Should().BeFalse();

        _host.IsOpen.Should().BeFalse();
        _factory.Created.Should().BeEmpty("no window is made, so nothing can ask for the keyboard");
        _diagnostics.Has("tug is showing").Should().BeTrue();
    }

    [Fact]
    public void Toggle_works_again_once_the_tug_is_gone()
    {
        _tug.IsTugShowing = true;
        _host.Toggle();
        _tug.IsTugShowing = false;

        _host.Toggle().Should().BeTrue();
    }

    [Fact]
    public void A_tug_firing_closes_an_open_flyout()
    {
        _host.Toggle();

        _tug.IsTugShowing = true;

        _host.IsOpen.Should().BeFalse();
        _factory.Last!.Closes.Should().Be(1);
        _host.Toggle().Should().BeFalse("and it cannot be reopened over the tug");
    }

    [Fact]
    public void A_tug_going_away_does_not_open_the_flyout_by_itself()
    {
        _tug.IsTugShowing = true;
        _tug.IsTugShowing = false;

        _host.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task The_real_controller_closes_the_flyout_when_a_takeover_is_due()
    {
        using var rig = new Rig();
        using var presence = new TugPresence(rig.Controller);
        using var host = new FlyoutHost(_factory, presence, _settings, _anchor, _monitors, new ImmediateDispatcher(), _time);
        host.Toggle().Should().BeTrue();
        presence.IsTugShowing.Should().BeFalse();

        await rig.Controller.OnTakeoverDueAsync(Requests.Meeting());

        presence.IsTugShowing.Should().BeTrue();
        host.IsOpen.Should().BeFalse();
        host.Toggle().Should().BeFalse();
        rig.Engine.Terminal.Should().BeEmpty("the flyout sent nothing to the engine");
    }

    // ---- looks --------------------------------------------------------------------------------------------------

    [Fact]
    public void The_normal_look_is_acrylic_with_animation()
    {
        _host.Toggle();

        _factory.Last!.Look.Should().Be(new FlyoutLook(FlyoutBackdrop.Material, true, false));
    }

    [Fact]
    public void Reduced_transparency_chooses_the_solid_backdrop()
    {
        _settings.TransparencyEffectsEnabled = false;

        _host.Toggle();

        _factory.Last!.Look!.Backdrop.Should().Be(FlyoutBackdrop.Solid);
    }

    [Fact]
    public void A_contrast_theme_chooses_the_solid_backdrop()
    {
        _settings.HighContrast = true;

        _host.Toggle();

        _factory.Last!.Look.Should().Be(new FlyoutLook(FlyoutBackdrop.Solid, true, true));
    }

    [Fact]
    public void Reduced_motion_removes_the_opening_animation()
    {
        _settings.AnimationsEnabled = false;

        _host.Toggle();

        _factory.Last!.Look!.Animate.Should().BeFalse();
    }

    [Fact]
    public void A_settings_change_while_open_restyles_the_window()
    {
        _host.Toggle();

        _settings.TransparencyEffectsEnabled = false;
        _settings.Raise();

        _factory.Last!.Look!.Backdrop.Should().Be(FlyoutBackdrop.Solid);
        _factory.Last.Updates.Should().Be(1);
    }

    // ---- size and place -----------------------------------------------------------------------------------------

    [Fact]
    public void A_change_of_content_height_resizes_the_open_window_against_the_same_icon()
    {
        _host.Toggle();
        var asked = _anchor.Asked;

        _factory.Last!.Content = new SizeInDips(360, 600);
        _factory.Last.RaiseContentChanged();

        _factory.Last.Bounds!.Value.Height.Should().Be(520, "capped");
        _factory.Last.Bounds.Value.Bottom.Should().Be(1032 - 8, "still sitting on the taskbar");
        _anchor.Asked.Should().Be(asked, "the icon does not move while the flyout is open");
    }

    [Fact]
    public void Without_an_anchor_the_flyout_opens_in_the_corner_of_the_primary_work_area()
    {
        _anchor.Anchor = null;

        _host.Toggle();

        _factory.Last!.Bounds.Should().Be(new PixelRect(1920 - 8 - 360, 1032 - 8 - 300, 360, 300));
    }

    [Fact]
    public void Disposing_closes_an_open_flyout_and_stops_listening()
    {
        _host.Toggle();

        _host.Dispose();

        _factory.Last!.Closes.Should().Be(1);
        _tug.IsTugShowing = true;
        _host.Toggle().Should().BeFalse();
    }

    private sealed class SingleFactory(IFlyoutSurface surface) : IFlyoutSurfaceFactory
    {
        public IFlyoutSurface Create() => surface;
    }
}
