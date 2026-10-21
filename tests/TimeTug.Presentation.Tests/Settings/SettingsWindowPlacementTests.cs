using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tests.Settings;

public class SettingsWindowPlacementTests
{
    private static readonly MonitorInfo Primary = new("p", 0, 0, 1920, 1080, true) { WorkArea = new PixelRect(0, 0, 1920, 1040) };
    private static readonly MonitorInfo Left = new("l", -1280, 0, 1280, 1024, false);

    [Fact]
    public void A_remembered_window_that_is_still_on_a_display_is_used_as_it_was()
    {
        var remembered = new WindowGeometry(300, 120, 1000, 700);

        SettingsWindowPlacement.Compute(remembered, [Primary]).Should().Be(remembered);
    }

    [Fact]
    public void A_remembered_window_on_a_second_display_is_used_while_that_display_is_there_and_discarded_when_it_is_gone()
    {
        var remembered = new WindowGeometry(-1200, 60, 900, 600);

        SettingsWindowPlacement.Compute(remembered, [Primary, Left]).Should().Be(remembered);
        SettingsWindowPlacement.Compute(remembered, [Primary]).Should().NotBe(remembered, "off-screen geometry is discarded");
    }

    [Fact]
    public void With_nothing_remembered_the_window_is_centred_on_the_primary_work_area_at_the_default_size()
    {
        var placed = SettingsWindowPlacement.Compute(null, [Left, Primary]);

        placed.Width.Should().Be(780);
        placed.Height.Should().Be(560);
        placed.X.Should().Be((1920 - 780) / 2);
        placed.Y.Should().Be((1040 - 560) / 2);
    }

    [Fact]
    public void The_default_size_follows_the_displays_scale()
    {
        var scaled = Primary with { Dpi = 144 };

        var placed = SettingsWindowPlacement.Compute(null, [scaled]);

        placed.Width.Should().Be(1170);
        placed.Height.Should().Be(840);
    }

    [Fact]
    public void The_default_window_never_exceeds_a_small_work_area()
    {
        var small = new MonitorInfo("s", 0, 0, 1024, 600, true) { WorkArea = new PixelRect(0, 0, 1024, 560) };

        var placed = SettingsWindowPlacement.Compute(null, [small]);

        placed.Width.Should().BeLessThanOrEqualTo(1024);
        placed.Height.Should().BeLessThanOrEqualTo(560);
        placed.X.Should().BeGreaterThanOrEqualTo(0);
        placed.Y.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void A_remembered_size_below_the_minimum_is_raised_to_it()
    {
        var placed = SettingsWindowPlacement.Compute(new WindowGeometry(200, 100, 300, 200), [Primary]);

        placed.X.Should().Be(200);
        placed.Y.Should().Be(100);
        placed.Width.Should().Be(720);
        placed.Height.Should().Be(460);
    }

    [Fact]
    public void Without_any_display_there_is_still_a_usable_default()
    {
        var placed = SettingsWindowPlacement.Compute(null, []);

        placed.Width.Should().Be(780);
        placed.Height.Should().Be(560);
    }

    [Fact]
    public void The_default_and_minimum_sizes_are_the_macs()
    {
        SettingsWindowPlacement.MinimumSize.Should().Be(new SizeInDips(720, 460));
        SettingsWindowPlacement.DefaultSize.Should().Be(new SizeInDips(780, 560));
    }
}
