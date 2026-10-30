using System.Globalization;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tests.Flyout;
using TimeTug.Presentation.Tests.Tray;
using TimeTug.Presentation.Tests.Tug;
using TimeTug.Presentation.Tests.Welcome;

namespace TimeTug.Presentation.Tests.Chip;

internal sealed class FakeChipWindow : IChipWindow
{
    public bool IsShown { get; private set; }

    public bool Disposed { get; private set; }

    public bool IsDragging { get; set; }

    public PixelRect? Bounds { get; private set; }

    public int Shows { get; private set; }

    public int Hides { get; private set; }

    public List<PixelRect> Moves { get; } = [];

    public List<ChipPlan> Contents { get; } = [];

    public List<string> Announcements { get; } = [];

    public SizeInDips Content { get; set; } = new(200, 32);

    public event Action? Clicked;

    public event Action? JoinRequested;

    public event Action? HideRequested;

    public event Action? SettingsRequested;

    public event Action<PixelRect>? Dragged;

    public event Action? ResetRequested;

    public event Action? Closed;

    public SizeInDips MeasureContent() => Content;

    public void SetContent(ChipPlan plan) => Contents.Add(plan);

    public void Show(PixelRect bounds)
    {
        Shows++;
        IsShown = true;
        Bounds = bounds;
    }

    public void Move(PixelRect bounds)
    {
        Moves.Add(bounds);
        Bounds = bounds;
    }

    public void Hide()
    {
        Hides++;
        IsShown = false;
    }

    public void Announce(string text) => Announcements.Add(text);

    public void Dispose()
    {
        Disposed = true;
        IsShown = false;
    }

    public void RaiseClicked() => Clicked?.Invoke();

    public void RaiseJoin() => JoinRequested?.Invoke();

    public void RaiseHide() => HideRequested?.Invoke();

    public void RaiseSettings() => SettingsRequested?.Invoke();

    public void RaiseDragged(PixelRect dropped) => Dragged?.Invoke(dropped);

    public void RaiseReset() => ResetRequested?.Invoke();

    public void RaiseClosed() => Closed?.Invoke();
}

internal sealed class FakeChipWindowFactory : IChipWindowFactory
{
    public List<FakeChipWindow> Created { get; } = [];

    public FakeChipWindow? Last => Created.Count == 0 ? null : Created[^1];

    public bool ThrowOnCreate { get; set; }

    public IChipWindow Create()
    {
        if (ThrowOnCreate)
        {
            throw new InvalidOperationException("no window");
        }

        var window = new FakeChipWindow();
        Created.Add(window);
        return window;
    }
}

internal sealed class RecordingChipActions : IChipActions
{
    public int Toggles { get; private set; }

    public int SettingsOpened { get; private set; }

    public List<string> Joined { get; } = [];

    public void ToggleFlyout() => Toggles++;

    public ValueTask JoinAsync(string eventId)
    {
        Joined.Add(eventId);
        return ValueTask.CompletedTask;
    }

    public void OpenAppearanceSettings() => SettingsOpened++;
}

internal sealed class FakePollTicker : IPollTicker
{
    private Action? _tick;

    public TimeSpan? Interval { get; private set; }

    public int Started { get; private set; }

    public int Stopped { get; private set; }

    public bool Running => _tick is not null;

    public IDisposable Start(TimeSpan interval, Action tick)
    {
        Started++;
        Interval = interval;
        _tick = tick;
        return new Stop(this);
    }

    public void Tick() => _tick?.Invoke();

    private sealed class Stop(FakePollTicker owner) : IDisposable
    {
        public void Dispose()
        {
            owner.Stopped++;
            owner._tick = null;
        }
    }
}

public sealed class ChipHostTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeEngineClient _engine = new();
    private readonly FakeTimeProvider _time = new(Noon);
    private readonly FakeMinuteTicker _minutes = new();
    private readonly MemoryPresentationSettingsStore _store = new();
    private readonly FakeChipWindowFactory _factory = new();
    private readonly RecordingChipActions _actions = new();
    private readonly FakeTugPresence _tug = new();
    private readonly FakeNotifications _notifications = new();
    private readonly FakePollTicker _poll = new();
    private readonly FakeMonitors _monitors = new();
    private readonly FakeAnchor _anchor = new();
    private readonly RecordingTugDiagnostics _log = new();
    private readonly ChipViewModel _model;
    private readonly ChipHost _host;

    public ChipHostTests()
    {
        _monitors.Set(new MonitorInfo("m1", 0, 0, 1920, 1080, true) { WorkArea = new PixelRect(0, 0, 1920, 1040) });
        _model = new ChipViewModel(_engine, _store, _time, _minutes, new ReswLocalizer(), CultureInfo.GetCultureInfo("en-US"), _log);
        _host = new ChipHost(_model, _factory, _actions, _tug, _notifications, _poll, _monitors, _anchor, _store, new ImmediateDispatcher(), _log);
    }

    public void Dispose()
    {
        _host.Dispose();
        _model.Dispose();
    }

    private static AgendaItem Meeting(string id, string title, TimeSpan start, AgendaPhase phase = AgendaPhase.Upcoming, string? join = null) =>
        new(id, title, Noon + start, Noon + start + TimeSpan.FromMinutes(30), false, phase, "Work", "#3366CC", join is null ? null : new Uri(join), 0, false, null) { IsNext = true };

    private void Show(AgendaItem item, ChipMode mode = ChipMode.Always)
    {
        _store.Save(_store.Load() with { ChipMode = mode });
        _engine.Push(States.Empty() with { Agenda = [item] });
    }

    private void Tick(TimeSpan by)
    {
        _time.Advance(by);
        _minutes.Tick();
    }

    // ---- showing and closing ------------------------------------------------------------------------------------

    [Fact]
    public void A_visible_plan_opens_one_window_above_the_tray_icon_with_the_plans_text()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _factory.Created.Should().ContainSingle();
        var window = _factory.Last!;
        window.IsShown.Should().BeTrue();
        window.Contents.Should().ContainSingle().Which.Text.Should().Be("Planning · in 12 min");
        window.Bounds.Should().Be(new PixelRect(1724 - 200, 1040 - 8 - 32, 200, 32), "level with the icon's right edge, above the taskbar");
        _host.IsShown.Should().BeTrue();
    }

    [Fact]
    public void Nothing_opens_while_the_plan_is_hidden()
    {
        _engine.Push(States.Empty() with { Agenda = [Meeting("a", "Planning", TimeSpan.FromMinutes(12))] });
        _host.Start();

        _factory.Created.Should().BeEmpty();
        _host.HasWindow.Should().BeFalse();
        _poll.Started.Should().Be(0, "there is nothing to watch for");
    }

    [Fact]
    public void A_change_of_text_updates_the_window_without_opening_it_again()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(40)));

        Tick(TimeSpan.FromMinutes(1));

        _factory.Created.Should().ContainSingle();
        _factory.Last!.Shows.Should().Be(1);
        _factory.Last.Contents.Select(c => c.Countdown).Should().Equal("in 40 min", "in 39 min");
    }

    [Fact]
    public void A_wider_text_re_places_the_chip_so_it_stays_level_with_the_icon()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(40)));
        _factory.Last!.Content = new SizeInDips(260, 32);

        Tick(TimeSpan.FromMinutes(1));

        _factory.Last.Moves.Should().ContainSingle().Which.Should().Be(new PixelRect(1724 - 260, 1000, 260, 32));
    }

    [Fact]
    public void Turning_the_chip_off_closes_the_window_and_stops_watching()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;

        _store.Save(_store.Load() with { ChipMode = ChipMode.Off });

        window.Disposed.Should().BeTrue();
        _host.HasWindow.Should().BeFalse();
        _poll.Stopped.Should().Be(1);
    }

    [Fact]
    public void When_the_meeting_is_over_the_window_closes_and_the_next_one_opens_a_new_window()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(5)));
        var first = _factory.Last!;

        Tick(TimeSpan.FromMinutes(36));
        first.Disposed.Should().BeTrue();

        _engine.Push(States.Empty() with { Agenda = [Meeting("b", "Review", TimeSpan.FromMinutes(60))] });
        _factory.Created.Should().HaveCount(2);
        _factory.Last!.IsShown.Should().BeTrue();
    }

    [Fact]
    public void Quitting_closes_the_window_stops_the_poll_and_listens_to_nothing_more()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;

        _host.Dispose();
        _host.Dispose();

        window.Disposed.Should().BeTrue();
        _poll.Running.Should().BeFalse();
        _tug.IsTugShowing = false;
        _monitors.Raise();
        Tick(TimeSpan.FromMinutes(1));
        _factory.Created.Should().ContainSingle();
    }

    [Fact]
    public void A_window_that_cannot_be_made_is_logged_and_the_next_change_tries_again()
    {
        _factory.ThrowOnCreate = true;
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _host.HasWindow.Should().BeFalse();
        _log.Has("could not be created").Should().BeTrue();

        _factory.ThrowOnCreate = false;
        Tick(TimeSpan.FromMinutes(1));

        _host.IsShown.Should().BeTrue();
    }

    [Fact]
    public void A_window_closed_from_outside_is_made_again_at_the_next_change()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(40)));
        _factory.Last!.RaiseClosed();

        Tick(TimeSpan.FromMinutes(1));

        _factory.Created.Should().HaveCount(2);
        _factory.Last!.IsShown.Should().BeTrue();
    }

    // ---- the tug and full-screen apps ---------------------------------------------------------------------------

    [Fact]
    public void The_tug_hides_the_chip_and_its_going_brings_it_back()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;

        _tug.IsTugShowing = true;
        window.IsShown.Should().BeFalse();
        window.Hides.Should().Be(1);

        Tick(TimeSpan.FromMinutes(1));
        window.Shows.Should().Be(1, "it stays hidden while the tug shows, whatever changes");

        _tug.IsTugShowing = false;
        window.IsShown.Should().BeTrue();
        window.Shows.Should().Be(2);
        _factory.Created.Should().ContainSingle();
    }

    [Fact]
    public void The_chip_is_not_shown_at_all_when_a_tug_is_already_up()
    {
        _tug.IsTugShowing = true;
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _host.IsShown.Should().BeFalse();
        _factory.Created.Should().BeEmpty("no window is made for a chip that cannot be shown");
    }

    [Fact]
    public void A_full_screen_app_that_starts_while_the_chip_is_up_hides_it_within_one_poll()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;
        _poll.Interval.Should().Be(TimeSpan.FromSeconds(2));

        _notifications.CanShowFullScreen = false;
        _poll.Tick();

        window.IsShown.Should().BeFalse();
        _log.Has("full-screen").Should().BeTrue();
    }

    [Fact]
    public void When_the_full_screen_app_ends_the_chip_comes_back_at_the_next_poll()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;
        _notifications.CanShowFullScreen = false;
        _poll.Tick();

        _poll.Running.Should().BeTrue("the poll keeps going while the chip has something to show");
        _notifications.CanShowFullScreen = true;
        _poll.Tick();

        window.IsShown.Should().BeTrue();
        window.Shows.Should().Be(2);
    }

    [Fact]
    public void The_minute_tick_also_looks_at_full_screen()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(40)));
        _notifications.CanShowFullScreen = false;

        Tick(TimeSpan.FromMinutes(1));

        _factory.Last!.IsShown.Should().BeFalse();
    }

    [Fact]
    public void The_poll_runs_only_while_the_plan_is_visible_and_is_started_once()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(40)));
        Tick(TimeSpan.FromMinutes(1));
        Tick(TimeSpan.FromMinutes(1));
        _poll.Started.Should().Be(1);

        _store.Save(_store.Load() with { ChipMode = ChipMode.Off });
        _poll.Running.Should().BeFalse();
    }

    // ---- placement follows the displays -------------------------------------------------------------------------

    [Fact]
    public void A_monitor_that_is_removed_moves_the_chip_to_one_that_is_left()
    {
        var second = new MonitorInfo("m2", 1920, 0, 1920, 1080, false) { WorkArea = new PixelRect(1920, 0, 1920, 1040) };
        _monitors.Set(_monitors.Monitors[0], second);
        _anchor.Anchor = new AnchorRect(new PixelRect(3620, 1044, 24, 24), AnchorSource.Icon);
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        _factory.Last!.Bounds!.Value.X.Should().BeGreaterThan(1920, "on the second monitor");

        _monitors.Set(_monitors.Monitors[0]);
        _anchor.Anchor = new AnchorRect(new PixelRect(1700, 1044, 24, 24), AnchorSource.Icon);
        _monitors.Raise();

        var moved = _factory.Last.Bounds!.Value;
        new PixelRect(0, 0, 1920, 1040).Contains(moved.CenterX, moved.CenterY).Should().BeTrue();
        _factory.Last.Moves.Should().ContainSingle();
    }

    [Fact]
    public void A_dpi_change_resizes_and_places_the_chip_at_the_new_scale()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _monitors.Set(_monitors.Monitors[0] with { Dpi = 144 });
        _monitors.Raise();

        _factory.Last!.Bounds.Should().Be(new PixelRect(1724 - 300, 1040 - 12 - 48, 300, 48));
    }

    [Fact]
    public void A_taskbar_that_moves_to_the_left_moves_the_chip_beside_it()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _monitors.Set(_monitors.Monitors[0] with { WorkArea = new PixelRect(60, 0, 1860, 1080) });
        _anchor.Anchor = new AnchorRect(new PixelRect(8, 1000, 44, 44), AnchorSource.Icon);
        _anchor.RaiseChanged();

        _factory.Last!.Bounds.Should().Be(new PixelRect(68, 1080 - 8 - 32, 200, 32));
    }

    [Fact]
    public void Where_the_icon_is_not_known_the_chip_goes_to_the_corner_of_the_primary_monitor()
    {
        _anchor.Anchor = null;
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _factory.Last!.Bounds.Should().Be(new PixelRect(1920 - 8 - 200, 1000, 200, 32));
    }

    // ---- the person's hand --------------------------------------------------------------------------------------

    [Fact]
    public void A_left_click_toggles_the_flyout()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _factory.Last!.RaiseClicked();

        _actions.Toggles.Should().Be(1);
    }

    [Fact]
    public void The_menus_join_opens_the_meetings_link_through_the_flyouts_path()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12), join: "https://meet.example.test/abc"));

        _factory.Last!.RaiseJoin();

        _actions.Joined.Should().Equal("a");
    }

    [Fact]
    public void Join_with_no_link_does_nothing()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _factory.Last!.RaiseJoin();

        _actions.Joined.Should().BeEmpty();
    }

    [Fact]
    public void The_menus_hide_closes_the_chip_until_a_different_meeting_comes()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;

        window.RaiseHide();

        window.Disposed.Should().BeTrue();
        _host.HasWindow.Should().BeFalse();
        _engine.Push(States.Empty() with { Agenda = [Meeting("b", "Review", TimeSpan.FromMinutes(40))] });
        _host.IsShown.Should().BeTrue();
    }

    [Fact]
    public void The_menus_settings_opens_the_appearance_pane()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _factory.Last!.RaiseSettings();

        _actions.SettingsOpened.Should().Be(1);
    }

    [Fact]
    public void A_drag_is_remembered_for_the_monitor_it_ended_on_and_the_chip_stays_where_it_was_dropped()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(40)));
        var window = _factory.Last!;
        var dropped = new PixelRect(500, 300, 200, 32);

        window.RaiseDragged(dropped);
        Tick(TimeSpan.FromMinutes(1));

        _store.Load().ChipPositions.Should().Equal(new ChipPosition("m1", 500, 300));
        window.Moves.Should().BeEmpty("the chip is where the person left it");
    }

    [Fact]
    public void A_remembered_place_is_used_by_a_chip_that_opens_later()
    {
        _store.Save(_store.Load() with { ChipPositions = [new ChipPosition("m1", 500, 300)] });

        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));

        _factory.Last!.Bounds.Should().Be(new PixelRect(500, 300, 200, 32));
    }

    [Fact]
    public void The_chip_is_left_alone_while_the_person_holds_it()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(40)));
        var window = _factory.Last!;
        window.Content = new SizeInDips(260, 32);
        window.IsDragging = true;

        Tick(TimeSpan.FromMinutes(1));

        window.Moves.Should().BeEmpty();
    }

    [Fact]
    public void A_double_click_forgets_the_remembered_place_and_puts_the_chip_back()
    {
        _store.Save(_store.Load() with { ChipPositions = [new ChipPosition("m1", 500, 300), new ChipPosition("other", 1, 1)] });
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;

        window.RaiseReset();

        _store.Load().ChipPositions.Should().Equal(new ChipPosition("other", 1, 1));
        window.Bounds.Should().Be(new PixelRect(1724 - 200, 1000, 200, 32));
    }

    [Fact]
    public void A_double_click_on_a_chip_in_its_default_place_changes_nothing()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(12)));
        var window = _factory.Last!;
        var before = window.Bounds;

        window.RaiseReset();

        window.Bounds.Should().Be(before);
    }

    // ---- announcements ------------------------------------------------------------------------------------------

    [Fact]
    public void An_announcement_is_handed_to_the_window_once()
    {
        Show(Meeting("a", "Planning", TimeSpan.FromMinutes(31)));

        Tick(TimeSpan.FromMinutes(1));
        _monitors.Raise();
        _poll.Tick();

        _factory.Last!.Announcements.Should().Equal("Planning starts in 30 minutes");
    }

    // ---- privacy ------------------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_about_the_meeting_reaches_the_log()
    {
        Show(Meeting("a", "Quarterly layoffs review", TimeSpan.FromMinutes(12), join: "https://meet.example.test/private"));
        _tug.IsTugShowing = true;
        _tug.IsTugShowing = false;
        _notifications.CanShowFullScreen = false;
        _poll.Tick();

        _log.Entries.Should().NotBeEmpty();
        _log.Entries.Should().NotContain(e => e.Message.Contains("layoffs", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("example.test", StringComparison.OrdinalIgnoreCase));
    }
}
