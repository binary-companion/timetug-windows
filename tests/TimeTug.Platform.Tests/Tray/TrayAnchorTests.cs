using TimeTug.Platform.OsState;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tests.Tray;

public sealed class TrayAnchorTests
{
    private static readonly Guid Id = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly PixelRect IconBox = new(1700, 1044, 24, 24);
    private static readonly PixelRect Chevron = new(1640, 1044, 24, 24);
    private static readonly PixelRect Area = new(1600, 1032, 320, 48);

    private readonly FakeProbe _probe = new();
    private readonly RecordingDiagnostics _diagnostics = new();

    private TrayAnchor Anchor(Guid? id = null, IMonitorService? monitors = null) => new(() => id ?? Id, _probe, monitors, _diagnostics);

    [Fact]
    public void The_icons_own_rectangle_is_the_first_choice()
    {
        _probe.Icon = IconBox;
        _probe.Chevron = Chevron;
        _probe.Area = Area;

        var anchor = Anchor().TryGetAnchor();

        anchor.Should().Be(new AnchorRect(IconBox, AnchorSource.Icon));
        _probe.Asked.Should().Equal("icon:" + Id);
    }

    [Fact]
    public void An_icon_in_the_overflow_gets_the_overflow_button_instead()
    {
        _probe.Icon = null;
        _probe.Chevron = Chevron;
        _probe.Area = Area;

        Anchor().TryGetAnchor().Should().Be(new AnchorRect(Chevron, AnchorSource.OverflowButton));
        _probe.Asked.Should().Equal("icon:" + Id, "chevron");
    }

    [Fact]
    public void An_empty_icon_rectangle_counts_as_no_answer()
    {
        _probe.Icon = new PixelRect(0, 0, 0, 0);
        _probe.Chevron = Chevron;

        Anchor().TryGetAnchor()!.Source.Should().Be(AnchorSource.OverflowButton);
    }

    [Fact]
    public void Without_the_overflow_button_the_notification_area_is_used()
    {
        _probe.Area = Area;

        Anchor().TryGetAnchor().Should().Be(new AnchorRect(Area, AnchorSource.NotificationArea));
        _probe.Asked.Should().Equal("icon:" + Id, "chevron", "area");
    }

    [Fact]
    public void Nothing_at_all_gives_no_anchor()
    {
        Anchor().TryGetAnchor().Should().BeNull();
        _diagnostics.Entries.Should().Contain(e => e.Message.Contains("no tray anchor", StringComparison.Ordinal));
    }

    [Fact]
    public void Before_the_icon_exists_the_first_question_is_skipped()
    {
        _probe.Chevron = Chevron;

        new TrayAnchor(() => null, _probe).TryGetAnchor()!.Source.Should().Be(AnchorSource.OverflowButton);
        _probe.Asked.Should().Equal("chevron");
    }

    [Fact]
    public void A_question_that_throws_is_the_same_as_no_answer_and_is_recorded()
    {
        _probe.ThrowOnIcon = true;
        _probe.Area = Area;

        Anchor().TryGetAnchor()!.Source.Should().Be(AnchorSource.NotificationArea);
        _diagnostics.Entries.Should().Contain(e => e.Level == "warning" && e.Message.Contains("Icon", StringComparison.Ordinal));
    }

    [Fact]
    public void A_rectangle_on_no_monitor_is_rejected_as_stale()
    {
        var monitors = new FixedMonitors(new MonitorInfo("m1", 0, 0, 1920, 1080, true));
        _probe.Icon = new PixelRect(5000, 100, 24, 24);
        _probe.Chevron = Chevron;

        Anchor(monitors: monitors).TryGetAnchor()!.Source.Should().Be(AnchorSource.OverflowButton);
    }

    [Fact]
    public void A_rectangle_on_a_monitor_with_negative_coordinates_is_accepted()
    {
        var monitors = new FixedMonitors(new MonitorInfo("m1", 0, 0, 1920, 1080, true), new MonitorInfo("m2", -1920, 0, 1920, 1080, false));
        _probe.Icon = new PixelRect(-100, 1044, 24, 24);

        Anchor(monitors: monitors).TryGetAnchor()!.Source.Should().Be(AnchorSource.Icon);
    }

    [Fact]
    public void The_source_is_recorded_once_and_again_only_when_it_changes()
    {
        _probe.Icon = IconBox;
        var anchor = Anchor();
        anchor.TryGetAnchor();
        anchor.TryGetAnchor();
        _probe.Icon = null;
        _probe.Area = Area;
        anchor.TryGetAnchor();
        anchor.TryGetAnchor();

        _diagnostics.Entries.Where(e => e.Category == "tray").Select(e => e.Message).Should().Equal(
            "the flyout anchor comes from Icon",
            "the flyout anchor comes from NotificationArea");
    }

    private sealed class FakeProbe : ITaskbarProbe
    {
        public PixelRect? Icon { get; set; }

        public PixelRect? Chevron { get; set; }

        public PixelRect? Area { get; set; }

        public bool ThrowOnIcon { get; set; }

        public List<string> Asked { get; } = [];

        public PixelRect? IconRect(Guid iconId)
        {
            Asked.Add("icon:" + iconId);
            return ThrowOnIcon ? throw new InvalidOperationException("explorer is restarting") : Icon;
        }

        /// <summary>When set, asking for the overflow button waits for it (Explorer is slow or hung).</summary>
        public ManualResetEventSlim? ChevronGate { get; set; }

        public PixelRect? OverflowButtonRect()
        {
            lock (Asked)
            {
                Asked.Add("chevron");
            }

            ChevronGate?.Wait(TimeSpan.FromSeconds(20));
            return Chevron;
        }

        public PixelRect? NotificationAreaRect()
        {
            Asked.Add("area");
            return Area;
        }

        public string Describe() => string.Empty;
    }

    private sealed class FixedMonitors(params MonitorInfo[] monitors) : IMonitorService
    {
        public IReadOnlyList<MonitorInfo> Monitors { get; } = monitors;

        public MonitorInfo Primary => Monitors.First(m => m.IsPrimary);

        public event Action? Changed;

        public void Raise() => Changed?.Invoke();
    }

    // ---- not waiting for Explorer on the UI thread --------------------------------------------------------------

    private static async Task<bool> Completes(Action work, int milliseconds = 2000)
    {
        var task = Task.Run(work);
        return await Task.WhenAny(task, Task.Delay(milliseconds)) == task;
    }

    [Fact]
    public async Task Peek_does_not_wait_for_a_slow_overflow_button_and_uses_the_notification_area()
    {
        using var gate = new ManualResetEventSlim(false);
        _probe.ChevronGate = gate;
        _probe.Chevron = Chevron;
        _probe.Area = Area;
        using var anchor = Anchor();
        AnchorRect? peeked = null;

        var returned = await Completes(() => peeked = anchor.Peek());
        gate.Set();

        returned.Should().BeTrue("the UI thread never waits for UI Automation");
        peeked.Should().Be(new AnchorRect(Area, AnchorSource.NotificationArea));
        _probe.Asked.Should().NotContain("chevron");
    }

    [Fact]
    public void Peek_uses_the_icons_own_rectangle_when_Windows_gives_it_quickly()
    {
        _probe.Icon = IconBox;
        using var anchor = Anchor();

        anchor.Peek().Should().Be(new AnchorRect(IconBox, AnchorSource.Icon));
    }

    [Fact]
    public async Task Refresh_returns_at_once_and_reports_the_better_place_when_the_slow_look_ends()
    {
        using var gate = new ManualResetEventSlim(false);
        _probe.ChevronGate = gate;
        _probe.Chevron = Chevron;
        _probe.Area = Area;
        using var anchor = Anchor();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        anchor.Changed += () => changed.TrySetResult();
        anchor.Peek().Should().Be(new AnchorRect(Area, AnchorSource.NotificationArea));

        var returned = await Completes(anchor.Refresh);
        returned.Should().BeTrue("Refresh only starts the look");
        changed.Task.IsCompleted.Should().BeFalse("the overflow button has not answered yet");

        gate.Set();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        anchor.Peek().Should().Be(new AnchorRect(Chevron, AnchorSource.OverflowButton));
    }

    [Fact]
    public async Task A_fresh_look_that_finds_the_same_place_raises_nothing()
    {
        _probe.Icon = IconBox;
        using var anchor = Anchor();
        var raised = 0;
        anchor.Changed += () => Interlocked.Increment(ref raised);
        anchor.Peek();

        anchor.Refresh();
        await Task.Delay(300);

        raised.Should().Be(0);
    }

    [Fact]
    public async Task The_last_good_answer_is_served_without_asking_the_taskbar_again()
    {
        _probe.Chevron = Chevron;
        _probe.Area = Area;
        using var anchor = Anchor();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        anchor.Changed += () => changed.TrySetResult();
        anchor.Peek();
        anchor.Refresh();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        List<string> asked;
        lock (_probe.Asked)
        {
            asked = [.. _probe.Asked];
        }

        anchor.Peek().Should().Be(new AnchorRect(Chevron, AnchorSource.OverflowButton));
        anchor.Peek();

        lock (_probe.Asked)
        {
            _probe.Asked.Should().Equal(asked, "the cached answer needs no question");
        }
    }

    [Fact]
    public async Task A_display_change_makes_it_forget_the_last_good_answer()
    {
        var monitors = new FixedMonitors(new MonitorInfo("m1", 0, 0, 1920, 1080, true));
        _probe.Chevron = Chevron;
        _probe.Area = Area;
        using var anchor = Anchor(monitors: monitors);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        anchor.Changed += () => changed.TrySetResult();
        anchor.Peek();
        anchor.Refresh();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        anchor.Peek()!.Source.Should().Be(AnchorSource.OverflowButton);

        monitors.Raise();

        anchor.Peek()!.Source.Should().Be(AnchorSource.NotificationArea, "the old rectangle may be on a display that is gone");
    }

    [Fact]
    public async Task Invalidate_forgets_the_last_good_answer()
    {
        _probe.Chevron = Chevron;
        _probe.Area = Area;
        using var anchor = Anchor();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        anchor.Changed += () => changed.TrySetResult();
        anchor.Peek();
        anchor.Refresh();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        anchor.Invalidate();

        anchor.Peek()!.Source.Should().Be(AnchorSource.NotificationArea);
    }

    [Fact]
    public async Task A_fresh_look_that_fails_drops_the_cached_answer()
    {
        _probe.Chevron = Chevron;
        _probe.Area = Area;
        using var anchor = Anchor();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        anchor.Changed += () => changed.TrySetResult();
        anchor.Peek();
        anchor.Refresh();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        anchor.Peek()!.Source.Should().Be(AnchorSource.OverflowButton);

        // Explorer restarted: nothing answers any more.
        _probe.Chevron = null;
        _probe.Area = null;
        var again = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        anchor.Changed += () => again.TrySetResult();
        anchor.Refresh();
        await again.Task.WaitAsync(TimeSpan.FromSeconds(10));

        anchor.Peek().Should().BeNull("a stale rectangle is worse than none");
    }

    [Fact]
    public async Task Two_refreshes_at_once_make_one_look()
    {
        using var gate = new ManualResetEventSlim(false);
        _probe.ChevronGate = gate;
        _probe.Chevron = Chevron;
        using var anchor = Anchor();
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        anchor.Changed += () => changed.TrySetResult();
        anchor.Peek();

        anchor.Refresh();
        anchor.Refresh();
        gate.Set();
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(200);

        lock (_probe.Asked)
        {
            _probe.Asked.Count(a => a == "chevron").Should().Be(1);
        }
    }
}
