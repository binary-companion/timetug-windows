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

        public PixelRect? OverflowButtonRect()
        {
            Asked.Add("chevron");
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

        public event Action? Changed
        {
            add { }
            remove { }
        }
    }
}
