using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
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

    public event Action? SettingsRequested;

    public event Action<string>? AccountsRequested;

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

    public void RaiseSettingsRequested() => SettingsRequested?.Invoke();

    public void RaiseAccountsRequested(string connectionId) => AccountsRequested?.Invoke(connectionId);
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

    public int Refreshes { get; private set; }

    public int Invalidations { get; private set; }

    /// <summary>The blocking answer must never be asked for by the flyout (it runs on the UI thread).</summary>
    public AnchorRect? TryGetAnchor() => throw new InvalidOperationException("the flyout asked for the blocking anchor on the UI thread");

    public AnchorRect? Peek()
    {
        Asked++;
        return Anchor;
    }

    public void Refresh() => Refreshes++;

    public void Invalidate() => Invalidations++;

    public void RaiseChanged() => Changed?.Invoke();

    public event Action? Changed;
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
    public void The_gear_closes_the_flyout_and_asks_for_the_settings_window_once()
    {
        _host.Toggle();
        var asked = new List<bool>();
        _host.SettingsRequested += () => asked.Add(_host.IsOpen);

        _factory.Last!.RaiseSettingsRequested();

        asked.Should().Equal(false);
        _host.IsOpen.Should().BeFalse("the flyout has gone by the time the Settings window opens");
        _factory.Last.Closes.Should().Be(1);
    }

    [Fact]
    public void A_problem_row_that_opens_the_Accounts_pane_closes_the_flyout_and_names_the_account()
    {
        _host.Toggle();
        var asked = new List<(string Id, bool Open)>();
        _host.AccountsRequested += id => asked.Add((id, _host.IsOpen));

        _factory.Last!.RaiseAccountsRequested("conn-2");

        asked.Should().Equal(("conn-2", false));
        _factory.Last.Closes.Should().Be(1);
    }

    [Fact]
    public void An_Accounts_request_after_the_flyout_closed_asks_for_nothing()
    {
        _host.Toggle();
        var surface = _factory.Last!;
        _host.Toggle();
        var asked = 0;
        _host.AccountsRequested += _ => asked++;

        surface.RaiseAccountsRequested("conn-2");

        asked.Should().Be(0);
    }

    [Fact]
    public void A_gear_press_after_the_flyout_closed_asks_for_nothing()
    {
        _host.Toggle();
        var surface = _factory.Last!;
        _host.Toggle();
        var asked = 0;
        _host.SettingsRequested += () => asked++;

        surface.RaiseSettingsRequested();

        asked.Should().Be(0);
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
    public void A_wall_clock_stepped_back_does_not_make_the_icon_and_the_hotkey_do_nothing()
    {
        var time = new SteppingTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        using var host = new FlyoutHost(_factory, _tug, _settings, _anchor, _monitors, new ImmediateDispatcher(), time, _diagnostics);
        host.Toggle();
        _factory.Last!.RaiseDeactivated();

        // The clock is set back an hour; ten real seconds pass; the person clicks the icon (or presses the hotkey: both call Toggle).
        time.StepWallClock(TimeSpan.FromHours(-1));
        time.Advance(TimeSpan.FromSeconds(10));

        host.Toggle().Should().BeTrue("ten seconds after the flyout closed this is a new click");
    }

    [Fact]
    public void A_wall_clock_stepped_forward_does_not_turn_the_same_click_into_a_new_one()
    {
        var time = new SteppingTime(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero));
        using var host = new FlyoutHost(_factory, _tug, _settings, _anchor, _monitors, new ImmediateDispatcher(), time, _diagnostics);
        host.Toggle();
        _factory.Last!.RaiseDeactivated();

        time.StepWallClock(TimeSpan.FromHours(1));
        time.Advance(TimeSpan.FromMilliseconds(80));

        host.Toggle().Should().BeFalse("80 ms of real time have passed: it is the click that deactivated the flyout");
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

    // ---- the Appearance pane's choices ----------------------------------------------------------------------------

    private FlyoutHost HostWith(MemoryPresentationSettingsStore store) =>
        new(_factory, _tug, _settings, _anchor, _monitors, new ImmediateDispatcher(), _time, _diagnostics, store);

    [Theory]
    [InlineData(FlyoutBackdrop.Material)]
    [InlineData(FlyoutBackdrop.Solid)]
    public void The_chosen_backdrop_is_the_look_when_windows_allows_it(FlyoutBackdrop chosen)
    {
        using var host = HostWith(new MemoryPresentationSettingsStore(PresentationSettings.Defaults with { Backdrop = chosen }));

        host.Toggle();

        _factory.Last!.Look!.Backdrop.Should().Be(chosen);
    }

    [Theory]
    [InlineData(FlyoutBackdrop.Material)]
    [InlineData(FlyoutBackdrop.Solid)]
    public void Reduced_transparency_or_contrast_make_the_look_solid_whatever_was_chosen(FlyoutBackdrop chosen)
    {
        using var host = HostWith(new MemoryPresentationSettingsStore(PresentationSettings.Defaults with { Backdrop = chosen }));

        _settings.TransparencyEffectsEnabled = false;
        host.Toggle();
        _factory.Last!.Look!.Backdrop.Should().Be(FlyoutBackdrop.Solid);
        host.Close();

        _settings.TransparencyEffectsEnabled = true;
        _settings.HighContrast = true;
        host.Toggle();
        _factory.Last!.Look!.Backdrop.Should().Be(FlyoutBackdrop.Solid);
    }

    [Theory]
    [InlineData(AppTheme.UseSystem, false, false)]
    [InlineData(AppTheme.UseSystem, true, true)]
    [InlineData(AppTheme.Light, true, false)]
    [InlineData(AppTheme.Dark, false, true)]
    public void The_look_carries_the_theme_that_applies(AppTheme theme, bool systemDark, bool expectedDark)
    {
        _settings.AppsUseDarkTheme = systemDark;
        using var host = HostWith(new MemoryPresentationSettingsStore(PresentationSettings.Defaults with { Theme = theme }));

        host.Toggle();

        _factory.Last!.Look!.Dark.Should().Be(expectedDark);
    }

    [Fact]
    public void A_new_choice_while_open_restyles_the_window_once()
    {
        var store = new MemoryPresentationSettingsStore();
        using var host = HostWith(store);
        host.Toggle();

        store.Save(store.Load() with { Backdrop = FlyoutBackdrop.Solid });

        _factory.Last!.Look!.Backdrop.Should().Be(FlyoutBackdrop.Solid);
        _factory.Last.Updates.Should().Be(1);

        store.Save(store.Load() with { Theme = AppTheme.Dark });

        _factory.Last.Look!.Dark.Should().BeTrue();
        _factory.Last.Updates.Should().Be(2);
    }

    [Fact]
    public void A_save_that_changes_nothing_the_flyout_shows_does_not_touch_the_open_window()
    {
        var store = new MemoryPresentationSettingsStore();
        using var host = HostWith(store);
        host.Toggle();

        store.Save(store.Load() with { SettingsWindow = new WindowGeometry(10, 10, 900, 700) });
        store.Save(store.Load() with { TrayStyle = TrayIconStyle.IconOnly });

        _factory.Last!.Updates.Should().Be(0);
    }

    [Fact]
    public void A_system_theme_change_while_open_restyles_a_window_that_follows_the_system()
    {
        using var host = HostWith(new MemoryPresentationSettingsStore());
        host.Toggle();

        _settings.AppsUseDarkTheme = true;
        _settings.Raise();

        _factory.Last!.Look!.Dark.Should().BeTrue();
    }

    [Fact]
    public void A_choice_made_while_closed_applies_the_next_time_it_opens()
    {
        var store = new MemoryPresentationSettingsStore();
        using var host = HostWith(store);
        host.Toggle();
        host.Close();

        store.Save(store.Load() with { Backdrop = FlyoutBackdrop.Solid });
        host.Toggle();

        _factory.Last!.Look!.Backdrop.Should().Be(FlyoutBackdrop.Solid);
    }

    [Fact]
    public void After_it_is_disposed_the_host_no_longer_follows_the_store()
    {
        var store = new MemoryPresentationSettingsStore();
        var host = HostWith(store);
        host.Toggle();
        var surface = _factory.Last!;
        host.Dispose();

        store.Save(store.Load() with { Backdrop = FlyoutBackdrop.Solid });

        surface.Updates.Should().Be(0);
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
    public void Opening_uses_the_answer_that_is_ready_and_starts_a_fresh_look_in_the_background()
    {
        _host.Toggle();

        _factory.Last!.IsShown.Should().BeTrue();
        _anchor.Refreshes.Should().Be(1);
    }

    [Fact]
    public void A_fresh_look_that_finds_a_different_place_moves_the_open_flyout()
    {
        _host.Toggle();
        var before = _factory.Last!.Bounds;

        _anchor.Anchor = new AnchorRect(new PixelRect(1300, 1044, 24, 24), AnchorSource.OverflowButton);
        _anchor.RaiseChanged();

        _factory.Last.Bounds.Should().NotBe(before);
        _factory.Last.Bounds!.Value.CenterX.Should().BeLessThan(before!.Value.CenterX, "the flyout now sits next to the new place");
        _factory.Last.Updates.Should().Be(1);
    }

    [Fact]
    public void A_fresh_look_that_ends_after_the_flyout_closed_does_nothing()
    {
        _host.Toggle();
        _host.Close();
        var surface = _factory.Last!;

        _anchor.RaiseChanged();

        surface.Updates.Should().Be(0);
        _host.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void A_change_of_taskbar_settings_makes_the_anchor_forget_its_last_answer()
    {
        _settings.Raise();

        _anchor.Invalidations.Should().Be(1);
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
