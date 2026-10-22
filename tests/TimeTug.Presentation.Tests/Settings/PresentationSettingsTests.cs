using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tests.Settings;

public class PresentationSettingsTests
{
    private static PresentationSettings Read(Dictionary<string, object> values) =>
        PresentationSettingsCodec.Read(key => values.GetValueOrDefault(key));

    private static Dictionary<string, object> Written(PresentationSettings settings)
    {
        var values = new Dictionary<string, object>();
        foreach (var (key, value) in PresentationSettingsCodec.Write(settings))
        {
            if (value is not null)
            {
                values[key] = value;
            }
        }

        return values;
    }

    [Fact]
    public void A_fresh_install_has_the_defaults()
    {
        var settings = Read([]);

        settings.Should().Be(PresentationSettings.Defaults);
        settings.Theme.Should().Be(AppTheme.UseSystem);
        settings.TrayStyle.Should().Be(TrayIconStyle.IconWithMinuteDigit);
        settings.Backdrop.Should().Be(FlyoutBackdrop.Material);
        settings.SettingsWindow.Should().BeNull();
    }

    [Fact]
    public void Every_field_round_trips()
    {
        var all = new PresentationSettings(AppTheme.Dark, TrayIconStyle.IconOnly, FlyoutBackdrop.Solid, new WindowGeometry(-1920, 40, 1100, 760));

        Read(Written(all)).Should().Be(all);
        Read(Written(all with { Theme = AppTheme.Light })).Theme.Should().Be(AppTheme.Light);
    }

    [Fact]
    public void A_cleared_window_geometry_is_written_as_no_value_so_the_key_can_be_removed()
    {
        var pairs = PresentationSettingsCodec.Write(PresentationSettings.Defaults);

        pairs.Single(p => p.Key == PresentationSettingsCodec.SettingsWindowKey).Value.Should().BeNull();
    }

    [Theory]
    [InlineData("Purple")]
    [InlineData("")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("99")]
    [InlineData("dark")]
    public void A_theme_that_is_not_one_of_the_names_is_the_default_and_the_other_fields_survive(string corrupt)
    {
        var values = Written(new PresentationSettings(AppTheme.Dark, TrayIconStyle.IconOnly, FlyoutBackdrop.Solid, new WindowGeometry(1, 2, 300, 400)));
        values[PresentationSettingsCodec.ThemeKey] = corrupt;

        var settings = Read(values);

        settings.Theme.Should().Be(AppTheme.UseSystem);
        settings.TrayStyle.Should().Be(TrayIconStyle.IconOnly);
        settings.Backdrop.Should().Be(FlyoutBackdrop.Solid);
        settings.SettingsWindow.Should().Be(new WindowGeometry(1, 2, 300, 400));
    }

    [Fact]
    public void A_value_of_the_wrong_type_is_the_default()
    {
        var settings = Read(new Dictionary<string, object>
        {
            [PresentationSettingsCodec.ThemeKey] = 2,
            [PresentationSettingsCodec.TrayStyleKey] = true,
            [PresentationSettingsCodec.BackdropKey] = 1.5,
            [PresentationSettingsCodec.SettingsWindowKey] = 7,
        });

        settings.Should().Be(PresentationSettings.Defaults);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,4,5")]
    [InlineData("1,2,0,500")]
    [InlineData("1,2,300,-5")]
    [InlineData("1,2,x,500")]
    [InlineData("1,2,300,4.5")]
    [InlineData("999999999,2,300,500")]
    [InlineData("-999999999,2,300,500")]
    [InlineData("-2147483648,2,300,500")]
    [InlineData("1,-2147483648,300,500")]
    [InlineData("2147483647,2,300,500")]
    [InlineData("1,2147483647,300,500")]
    [InlineData("1,2,-2147483648,500")]
    [InlineData("1,2,300,-2147483648")]
    [InlineData("1,2,2147483647,500")]
    [InlineData("1,2,300,2147483647")]
    [InlineData("1,2,-300,500")]
    [InlineData("-2147483648,-2147483648,-2147483648,-2147483648")]
    [InlineData("")]
    public void A_corrupt_window_geometry_is_no_geometry(string corrupt)
    {
        WindowGeometry.FromStorage(corrupt).Should().BeNull();
        Read(new Dictionary<string, object> { [PresentationSettingsCodec.SettingsWindowKey] = corrupt }).SettingsWindow.Should().BeNull();
    }

    [Fact]
    public void Geometry_is_kept_in_invariant_digits()
    {
        new WindowGeometry(-5, 10, 1280, 720).ToStorage().Should().Be("-5,10,1280,720");
        WindowGeometry.FromStorage("-5,10,1280,720").Should().Be(new WindowGeometry(-5, 10, 1280, 720));
    }

    [Fact]
    public void The_memory_store_raises_Changed_once_per_save_after_the_new_values_can_be_read()
    {
        var store = new MemoryPresentationSettingsStore();
        var seen = new List<PresentationSettings>();
        store.Changed += () => seen.Add(store.Load());

        store.Save(PresentationSettings.Defaults with { Theme = AppTheme.Dark });
        store.Save(PresentationSettings.Defaults with { Theme = AppTheme.Light });

        seen.Select(s => s.Theme).Should().Equal(AppTheme.Dark, AppTheme.Light);
    }

    // ---- is a remembered window still reachable ------------------------------------------------------------------

    private static readonly MonitorInfo Primary = new("primary", 0, 0, 1920, 1080, true) { WorkArea = new PixelRect(0, 0, 1920, 1040) };
    private static readonly MonitorInfo Left = new("left", -1280, 0, 1280, 1024, false);

    [Fact]
    public void A_window_on_a_connected_display_is_valid()
    {
        new WindowGeometry(200, 100, 1000, 700).ValidFor([Primary]).Should().BeTrue();
        new WindowGeometry(-1200, 50, 900, 600).ValidFor([Primary, Left]).Should().BeTrue("a display left of the primary one has negative coordinates");
    }

    [Fact]
    public void A_window_on_a_display_that_is_gone_is_discarded()
    {
        new WindowGeometry(-1200, 50, 900, 600).ValidFor([Primary]).Should().BeFalse();
        new WindowGeometry(5000, 100, 800, 600).ValidFor([Primary]).Should().BeFalse();
        new WindowGeometry(200, 2000, 800, 600).ValidFor([Primary]).Should().BeFalse();
    }

    [Fact]
    public void A_window_whose_title_bar_is_only_a_sliver_on_screen_is_discarded()
    {
        new WindowGeometry(1900, 100, 800, 600).ValidFor([Primary]).Should().BeFalse("20 pixels of title bar cannot be grabbed");
        new WindowGeometry(1700, 100, 800, 600).ValidFor([Primary]).Should().BeTrue("220 pixels of the title bar are reachable");
        new WindowGeometry(200, 1030, 800, 600).ValidFor([Primary]).Should().BeFalse("the title bar is under the taskbar");
    }

    [Fact]
    public void A_geometry_without_an_area_is_never_valid()
    {
        new WindowGeometry(10, 10, 0, 500).ValidFor([Primary]).Should().BeFalse();
        new WindowGeometry(10, 10, 500, 0).ValidFor([Primary]).Should().BeFalse();
        new WindowGeometry(200, 100, 1000, 700).ValidFor([]).Should().BeFalse("no display at all");
    }
}
