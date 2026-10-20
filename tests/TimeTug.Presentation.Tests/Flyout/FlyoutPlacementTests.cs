using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Presentation.Tests.Flyout;

public sealed class FlyoutPlacementTests
{
    private static readonly SizeInDips Medium = new(360, 300);

    /// <summary>A 1920x1080 display at 100 percent whose taskbar takes 48 pixels from the given side.</summary>
    private static MonitorInfo Display(string id = "m1", int x = 0, int y = 0, int width = 1920, int height = 1080, bool primary = true, int dpi = 96, string edge = "bottom", int taskbar = 48)
    {
        var work = edge switch
        {
            "bottom" => new PixelRect(x, y, width, height - taskbar),
            "top" => new PixelRect(x, y + taskbar, width, height - taskbar),
            "left" => new PixelRect(x + taskbar, y, width - taskbar, height),
            "right" => new PixelRect(x, y, width - taskbar, height),
            _ => new PixelRect(x, y, width, height),
        };
        return new MonitorInfo(id, x, y, width, height, primary) { WorkArea = work, Dpi = dpi };
    }

    private static PixelRect Icon(int x, int y, int size = 24) => new(x, y, size, size);

    // ---- the taskbar on each side ---------------------------------------------------------------------------------

    [Fact]
    public void Above_a_bottom_taskbar_centred_on_the_icon()
    {
        var result = FlyoutPlacement.Compute(Icon(1600, 1044), Medium, [Display()]);

        result.Should().Be(new PixelRect(1612 - 180, 1032 - 8 - 300, 360, 300), "the icon's centre is 1612; the gap is 8 above the work area, which ends where the taskbar begins");
    }

    [Fact]
    public void Below_a_top_taskbar()
    {
        var result = FlyoutPlacement.Compute(Icon(1600, 12), Medium, [Display(edge: "top")]);

        result.Should().Be(new PixelRect(1612 - 180, 48 + 8, 360, 300));
    }

    [Fact]
    public void Beside_a_left_taskbar_centred_on_the_icon_vertically()
    {
        var result = FlyoutPlacement.Compute(Icon(12, 600), Medium, [Display(edge: "left")]);

        result.Should().Be(new PixelRect(48 + 8, 612 - 150, 360, 300));
    }

    [Fact]
    public void Beside_a_right_taskbar()
    {
        var result = FlyoutPlacement.Compute(Icon(1884, 600), Medium, [Display(edge: "right")]);

        result.Should().Be(new PixelRect(1872 - 8 - 360, 612 - 150, 360, 300));
    }

    [Fact]
    public void A_taskbar_that_hides_itself_reserves_nothing_so_the_nearest_screen_edge_decides()
    {
        var hidden = Display(edge: "hidden");

        FlyoutPlacement.Compute(Icon(1600, 1050), Medium, [hidden]).Should().Be(new PixelRect(1612 - 180, 1050 - 8 - 300, 360, 300), "opens above the icon on a bottom edge");
        FlyoutPlacement.Compute(Icon(1600, 6), Medium, [hidden]).Should().Be(new PixelRect(1612 - 180, 30 + 8, 360, 300), "and below it on a top edge");
    }

    // ---- clamping -------------------------------------------------------------------------------------------------

    [Fact]
    public void An_icon_in_the_bottom_right_corner_pushes_the_flyout_against_the_work_area()
    {
        var result = FlyoutPlacement.Compute(Icon(1890, 1050), Medium, [Display()]);

        result.X.Should().Be(1920 - 8 - 360, "8 from the right edge");
        result.Bottom.Should().Be(1032 - 8);
    }

    [Fact]
    public void An_icon_in_the_bottom_left_corner_pushes_it_against_the_left_edge()
    {
        FlyoutPlacement.Compute(Icon(4, 1044), Medium, [Display()]).X.Should().Be(8);
    }

    [Fact]
    public void Beside_a_side_taskbar_the_flyout_slides_down_when_the_icon_is_at_the_top()
    {
        var result = FlyoutPlacement.Compute(Icon(12, 4), Medium, [Display(edge: "left")]);

        result.Y.Should().Be(8);
    }

    // ---- several monitors -----------------------------------------------------------------------------------------

    [Fact]
    public void A_second_monitor_to_the_left_has_negative_coordinates_and_its_own_taskbar()
    {
        var left = Display("m2", x: -1920, primary: false);
        var primary = Display();

        var result = FlyoutPlacement.Compute(Icon(-300, 1044), Medium, [primary, left]);

        result.Should().Be(new PixelRect(-288 - 180, 1032 - 8 - 300, 360, 300));
        left.Work.Contains(result.X, result.Y).Should().BeTrue();
    }

    [Fact]
    public void An_icon_on_the_far_left_monitor_corner_stays_on_that_monitor()
    {
        var left = Display("m2", x: -1920, primary: false);

        var result = FlyoutPlacement.Compute(Icon(-1916, 1044), Medium, [Display(), left]);

        result.X.Should().Be(-1920 + 8, "clamped to the left monitor, not spilling onto nothing");
    }

    [Fact]
    public void A_monitor_above_the_primary_has_negative_y()
    {
        var above = Display("m2", x: 0, y: -1080, primary: false);

        var result = FlyoutPlacement.Compute(Icon(900, -36), Medium, [Display(), above]);

        result.Should().Be(new PixelRect(912 - 180, -48 - 8 - 300, 360, 300));
    }

    [Fact]
    public void The_scale_is_the_one_of_the_monitor_that_holds_the_icon_not_the_primary_one()
    {
        var primary = Display(dpi: 192);
        var second = Display("m2", x: 1920, width: 2560, height: 1440, primary: false, dpi: 96, taskbar: 40);

        var result = FlyoutPlacement.Compute(Icon(4300, 1396), Medium, [primary, second]);

        result.Width.Should().Be(360, "100 percent scaling on the second monitor");
        result.Height.Should().Be(300);
    }

    [Fact]
    public void An_anchor_on_the_primary_monitor_uses_the_primary_scale_even_when_another_is_sharper()
    {
        var primary = Display(dpi: 96);
        var second = Display("m2", x: 1920, primary: false, dpi: 192);

        FlyoutPlacement.Compute(Icon(1600, 1044), Medium, [primary, second]).Width.Should().Be(360);
        FlyoutPlacement.Compute(Icon(3700, 1044), Medium, [primary, second]).Width.Should().Be(720);
    }

    [Fact]
    public void An_anchor_that_is_on_no_monitor_any_more_uses_the_nearest_one()
    {
        var result = FlyoutPlacement.Compute(Icon(2500, 1044), Medium, [Display()]);

        result.Right.Should().BeLessThanOrEqualTo(1920 - 8);
        result.Bottom.Should().Be(1032 - 8);
    }

    // ---- scaling --------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(96, 360, 300, 8)]
    [InlineData(120, 450, 375, 10)]
    [InlineData(144, 540, 450, 12)]
    [InlineData(192, 720, 600, 16)]
    public void The_size_and_the_gap_follow_the_monitors_scale(int dpi, int width, int height, int gap)
    {
        // A display of 1920 x 1080 DIP at each scale.
        var scale = dpi / 96.0;
        var monitor = Display(width: (int)(1920 * scale), height: (int)(1080 * scale), dpi: dpi, taskbar: (int)(48 * scale));
        var icon = Icon((int)(1600 * scale), (int)(1044 * scale), (int)(24 * scale));

        var result = FlyoutPlacement.Compute(icon, Medium, [monitor]);

        (result.Width, result.Height).Should().Be((width, height));
        result.Bottom.Should().Be(monitor.Work.Bottom - gap);
    }

    // ---- height ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Content_shorter_than_the_limit_gets_its_own_height()
    {
        FlyoutPlacement.Compute(Icon(1600, 1044), new SizeInDips(360, 180), [Display()]).Height.Should().Be(180);
    }

    [Fact]
    public void Content_taller_than_the_limit_is_capped_at_520_dips()
    {
        var result = FlyoutPlacement.Compute(Icon(1600, 1044), new SizeInDips(360, 2000), [Display()]);

        result.Height.Should().Be(520);
        result.Bottom.Should().Be(1032 - 8);
    }

    [Fact]
    public void Content_exactly_at_the_limit_is_not_changed()
    {
        FlyoutPlacement.Compute(Icon(1600, 1044), new SizeInDips(360, 520), [Display()]).Height.Should().Be(520);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void Content_with_no_height_still_gets_a_visible_window(double height)
    {
        FlyoutPlacement.Compute(Icon(1600, 1044), new SizeInDips(360, height), [Display()]).Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void The_width_is_always_360_dips_whatever_the_content_says()
    {
        FlyoutPlacement.Compute(Icon(1600, 1044), new SizeInDips(900, 300), [Display()]).Width.Should().Be(360);
    }

    [Fact]
    public void A_work_area_smaller_than_the_flyout_shrinks_it_and_keeps_it_inside()
    {
        var small = Display(width: 400, height: 500, taskbar: 40);

        var result = FlyoutPlacement.Compute(Icon(300, 470), new SizeInDips(360, 520), [small]);

        small.Work.Contains(result.X, result.Y).Should().BeTrue();
        result.Right.Should().BeLessThanOrEqualTo(small.Work.Right);
        result.Bottom.Should().BeLessThanOrEqualTo(small.Work.Bottom);
        result.X.Should().BeGreaterThanOrEqualTo(0);
        result.Y.Should().BeGreaterThanOrEqualTo(0);
    }

    // ---- the anchor is not available -------------------------------------------------------------------------------

    [Fact]
    public void Without_an_anchor_the_flyout_goes_to_the_bottom_right_of_the_primary_work_area()
    {
        var other = Display("m2", x: -1920, primary: false);

        var result = FlyoutPlacement.Compute(null, Medium, [other, Display()]);

        result.Should().Be(new PixelRect(1920 - 8 - 360, 1032 - 8 - 300, 360, 300));
    }

    [Fact]
    public void Without_an_anchor_and_without_a_primary_flag_the_first_monitor_is_used()
    {
        var only = Display(primary: false);

        FlyoutPlacement.Compute(null, Medium, [only]).X.Should().Be(1920 - 8 - 360);
    }

    [Fact]
    public void With_no_monitors_at_all_it_still_answers()
    {
        var result = FlyoutPlacement.Compute(Icon(10, 10), Medium, []);

        (result.Width, result.Height).Should().Be((360, 300));
    }

    [Fact]
    public void A_monitor_without_a_work_area_is_treated_as_all_work_area()
    {
        var plain = new MonitorInfo("m", 0, 0, 1920, 1080, true);

        var result = FlyoutPlacement.Compute(Icon(1600, 1056), Medium, [plain]);

        result.Bottom.Should().Be(1056 - 8, "opens above the icon");
    }

    // ---- every placement stays on its monitor -----------------------------------------------------------------------

    [Fact]
    public void Wherever_the_icon_is_the_flyout_stays_inside_the_work_area_of_its_monitor()
    {
        var monitors = new[] { Display(), Display("m2", x: -1920, primary: false, edge: "left"), Display("m3", x: 1920, width: 2560, height: 1440, primary: false, dpi: 144, edge: "right", taskbar: 60) };
        var random = new Random(12);
        for (var i = 0; i < 400; i++)
        {
            var monitor = monitors[random.Next(monitors.Length)];
            var icon = Icon(random.Next(monitor.X - 20, monitor.Bounds.Right + 20), random.Next(monitor.Y - 20, monitor.Bounds.Bottom + 20));
            var content = new SizeInDips(360, random.Next(40, 900));

            var result = FlyoutPlacement.Compute(icon, content, monitors);

            var home = FlyoutPlacement.MonitorFor(icon, monitors);
            result.X.Should().BeGreaterThanOrEqualTo(home.Work.X, $"{icon}");
            result.Y.Should().BeGreaterThanOrEqualTo(home.Work.Y, $"{icon}");
            result.Right.Should().BeLessThanOrEqualTo(home.Work.Right, $"{icon}");
            result.Bottom.Should().BeLessThanOrEqualTo(home.Work.Bottom, $"{icon}");
        }
    }
}
