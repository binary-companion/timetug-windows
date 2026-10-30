using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;

namespace TimeTug.Presentation.Tests.Chip;

public class ChipPlacementTests
{
    private static readonly SizeInDips Size = new(200, 32);
    private static readonly IReadOnlyList<ChipPosition> NoneSaved = [];

    /// <summary>A 1920 x 1080 primary monitor at 100 percent with the taskbar (40 px) at the bottom.</summary>
    private static readonly MonitorInfo Main = new("main", 0, 0, 1920, 1080, true) { WorkArea = new PixelRect(0, 0, 1920, 1040) };

    private static readonly PixelRect BottomIcon = new(1700, 1044, 24, 32);

    private static PixelRect Compute(IReadOnlyList<MonitorInfo> monitors, PixelRect? anchor, IReadOnlyList<ChipPosition>? saved = null, SizeInDips? size = null) =>
        ChipPlacement.Compute(saved ?? NoneSaved, monitors, anchor, size ?? Size);

    // ---- the default place --------------------------------------------------------------------------------------

    [Fact]
    public void With_the_taskbar_at_the_bottom_it_sits_above_the_notification_area_level_with_the_icons_right_edge()
    {
        var rect = Compute([Main], BottomIcon);

        rect.Should().Be(new PixelRect(1724 - 200, 1040 - 8 - 32, 200, 32));
    }

    [Fact]
    public void With_the_taskbar_on_top_it_sits_below_it()
    {
        var monitor = Main with { WorkArea = new PixelRect(0, 40, 1920, 1040) };

        Compute([monitor], new PixelRect(1700, 4, 24, 32)).Should().Be(new PixelRect(1724 - 200, 40 + 8, 200, 32));
    }

    [Fact]
    public void With_the_taskbar_on_the_left_it_sits_beside_it_at_the_bottom()
    {
        var monitor = Main with { WorkArea = new PixelRect(60, 0, 1860, 1080) };

        Compute([monitor], new PixelRect(8, 1000, 44, 44)).Should().Be(new PixelRect(60 + 8, 1080 - 8 - 32, 200, 32));
    }

    [Fact]
    public void With_the_taskbar_on_the_right_it_sits_beside_it_at_the_bottom()
    {
        var monitor = Main with { WorkArea = new PixelRect(0, 0, 1860, 1080) };

        Compute([monitor], new PixelRect(1868, 1000, 44, 44)).Should().Be(new PixelRect(1860 - 8 - 200, 1080 - 8 - 32, 200, 32));
    }

    [Fact]
    public void A_taskbar_that_hides_itself_reserves_nothing_and_the_chip_sits_at_the_bottom_edge()
    {
        var rect = Compute([Main with { WorkArea = null }], new PixelRect(1700, 1060, 24, 20));

        rect.Y.Should().Be(1080 - 8 - 32);
    }

    [Theory]
    [InlineData(96, 200, 32, 8)]
    [InlineData(144, 300, 48, 12)]
    [InlineData(192, 400, 64, 16)]
    public void The_size_and_the_gap_scale_with_the_monitor(int dpi, int width, int height, int margin)
    {
        var monitor = Main with { Dpi = dpi };

        var rect = Compute([monitor], null);

        rect.Should().Be(new PixelRect(1920 - margin - width, 1040 - margin - height, width, height));
    }

    [Fact]
    public void Without_an_anchor_it_goes_to_the_bottom_right_of_the_primary_monitor()
    {
        var side = new MonitorInfo("side", 1920, 0, 1280, 1024, false);

        Compute([side, Main], null).Should().Be(new PixelRect(1920 - 8 - 200, 1040 - 8 - 32, 200, 32));
    }

    [Fact]
    public void On_a_second_monitor_to_the_left_the_coordinates_are_negative()
    {
        var left = new MonitorInfo("left", -1920, 0, 1920, 1080, false) { WorkArea = new PixelRect(-1920, 0, 1920, 1040) };

        var rect = Compute([Main, left], new PixelRect(-220, 1044, 24, 32));

        rect.Should().Be(new PixelRect(-196 - 200, 1040 - 8 - 32, 200, 32));
        left.Work.Contains(rect.CenterX, rect.CenterY).Should().BeTrue();
    }

    [Fact]
    public void An_anchor_beyond_every_monitor_uses_the_nearest_one()
    {
        Compute([Main], new PixelRect(5000, 1044, 24, 32)).Should().Be(new PixelRect(1920 - 8 - 200, 1040 - 8 - 32, 200, 32));
    }

    [Fact]
    public void With_no_monitors_it_answers_a_rectangle_at_the_origin()
    {
        Compute([], BottomIcon).Should().Be(new PixelRect(0, 0, 200, 32));
    }

    [Fact]
    public void It_is_never_larger_than_the_work_area_and_never_outside_it()
    {
        var tiny = new MonitorInfo("tiny", 0, 0, 100, 20, true);

        var rect = Compute([tiny], null);

        rect.Should().Be(new PixelRect(0, 0, 100, 20));
    }

    // ---- a remembered place -------------------------------------------------------------------------------------

    [Fact]
    public void A_remembered_place_is_the_top_left_in_dips_from_the_work_area_origin()
    {
        var monitor = Main with { WorkArea = new PixelRect(0, 40, 1920, 1040), Dpi = 144 };

        var rect = Compute([monitor], BottomIcon, [new ChipPosition("main", 100, 200)]);

        rect.Should().Be(new PixelRect(150, 40 + 300, 300, 48));
    }

    [Fact]
    public void A_remembered_place_on_a_monitor_to_the_left_is_relative_to_that_monitor()
    {
        var left = new MonitorInfo("left", -1920, 0, 1920, 1080, false) { WorkArea = new PixelRect(-1920, 0, 1920, 1040) };

        var rect = Compute([Main, left], BottomIcon, [new ChipPosition("left", 50, 60)]);

        rect.Should().Be(new PixelRect(-1920 + 50, 60, 200, 32));
    }

    [Fact]
    public void A_place_on_a_monitor_that_is_gone_falls_back_to_the_default()
    {
        var rect = Compute([Main], BottomIcon, [new ChipPosition("unplugged", 50, 60)]);

        rect.Should().Be(Compute([Main], BottomIcon));
    }

    [Fact]
    public void The_newest_valid_place_wins_and_one_that_no_longer_fits_is_skipped()
    {
        var shrunk = Main with { Width = 800, Height = 600, WorkArea = new PixelRect(0, 0, 800, 560) };
        var saved = new[] { new ChipPosition("main", 10, 10), new ChipPosition("main", 1500, 900) };

        var rect = Compute([shrunk], BottomIcon, saved);

        rect.X.Should().Be(10);
        rect.Y.Should().Be(10);
    }

    [Fact]
    public void A_place_that_is_partly_off_the_work_area_is_slid_back_in()
    {
        var rect = Compute([Main], BottomIcon, [new ChipPosition("main", 1750, 1020)]);

        rect.Should().Be(new PixelRect(1920 - 200, 1040 - 32, 200, 32));
    }

    [Fact]
    public void A_place_whose_centre_is_off_every_work_area_is_ignored()
    {
        var rect = Compute([Main], BottomIcon, [new ChipPosition("main", 5000, 5000)]);

        rect.Should().Be(Compute([Main], BottomIcon));
    }

    [Fact]
    public void A_place_over_the_taskbar_is_not_used()
    {
        // The work area ends where the taskbar starts; a centre over the taskbar is not on the work area.
        var rect = Compute([Main], BottomIcon, [new ChipPosition("main", 800, 1050)]);

        rect.Should().Be(Compute([Main], BottomIcon));
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void A_place_dragged_at_one_scale_lands_in_the_same_place_at_another(int restoredDpi)
    {
        var dragged = Main with { Dpi = 144 };
        var dropped = new PixelRect(300, 450, 300, 48);

        var position = ChipPlacement.ToPosition(dropped, [dragged]);
        var restored = Compute([Main with { Dpi = restoredDpi }], BottomIcon, [position!.Value]);

        position.Should().Be(new ChipPosition("main", 200, 300));
        var scale = restoredDpi / 96.0;
        restored.X.Should().Be((int)Math.Round(200 * scale, MidpointRounding.AwayFromZero));
        restored.Y.Should().Be((int)Math.Round(300 * scale, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void A_dropped_place_is_measured_from_the_work_area_of_the_monitor_that_holds_its_centre()
    {
        var left = new MonitorInfo("left", -1920, 0, 1920, 1080, false) { WorkArea = new PixelRect(-1920, 40, 1920, 1040) };

        ChipPlacement.ToPosition(new PixelRect(-1800, 100, 200, 32), [Main, left]).Should().Be(new ChipPosition("left", 120, 60));
    }

    [Fact]
    public void A_dropped_place_beyond_every_monitor_belongs_to_the_nearest_and_no_monitors_gives_nothing()
    {
        ChipPlacement.ToPosition(new PixelRect(5000, 100, 200, 32), [Main])!.Value.MonitorId.Should().Be("main");
        ChipPlacement.ToPosition(new PixelRect(0, 0, 10, 10), []).Should().BeNull();
    }
}
