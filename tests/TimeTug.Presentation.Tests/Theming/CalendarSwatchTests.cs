using TimeTug.Presentation.Theming;

namespace TimeTug.Presentation.Tests.Theming;

public class CalendarSwatchTests
{
    // What the Settings card and the flyout card are in Windows 11's light and dark themes (WinUI's card fill over the Mica base) and the
    // theme's secondary text colour on them, as the app computes them.
    private static readonly Rgb LightSurface = new(0xFB, 0xFB, 0xFB);
    private static readonly Rgb LightForeground = new Rgb(0, 0, 0).Over(LightSurface, 0x9E);
    private static readonly Rgb DarkSurface = new(0x2B, 0x2B, 0x2B);
    private static readonly Rgb DarkForeground = Rgb.White.Over(DarkSurface, 0xC5);

    public static TheoryData<bool> Themes => new() { false, true };

    private static (Rgb Surface, Rgb Foreground) Theme(bool dark) => dark ? (DarkSurface, DarkForeground) : (LightSurface, LightForeground);

    public static TheoryData<string, bool> Colours
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var hex in new[] { "#000000", "#0A0A0A", "#0D1429", "#1A237E", "#4285F4", "#0B8043", "#E67C73", "#808080", "#FFFF00", "#FAFAFA", "#FFFFFF", "#2B2B2B", "#FBFBFB" })
            {
                data.Add(hex, false);
                data.Add(hex, true);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Colours))]
    public void A_checked_box_always_reads_the_mark_on_the_fill_and_the_edge_on_the_page(string hex, bool dark)
    {
        var (surface, foreground) = Theme(dark);

        var swatch = CalendarSwatch.For(hex, surface, foreground);

        ColorContrast.Ratio(swatch.Mark, swatch.Fill).Should().BeGreaterThanOrEqualTo(ColorContrast.TextMinimum, "the check mark must show on the fill");
        ColorContrast.Ratio(swatch.Edge, surface).Should().BeGreaterThanOrEqualTo(ColorContrast.GraphicMinimum, "the box must show on the page");
        swatch.UsesCalendarColor.Should().BeTrue();
        swatch.Fill.ToHex().Should().Be(hex);
    }

    [Fact]
    public void A_dark_calendar_on_a_dark_page_gets_a_white_mark_and_the_theme_foreground_edge()
    {
        var swatch = CalendarSwatch.For("#0A0A0A", DarkSurface, DarkForeground);

        swatch.Mark.Should().Be(Rgb.White);
        swatch.Edge.Should().Be(DarkForeground, "the calendar's colour is invisible against the dark card");
    }

    [Fact]
    public void A_light_calendar_on_a_light_page_gets_a_near_black_mark_and_the_theme_foreground_edge()
    {
        var swatch = CalendarSwatch.For("#FFFFFF", LightSurface, LightForeground);

        swatch.Mark.ToHex().Should().Be("#101010");
        swatch.Edge.Should().Be(LightForeground, "white is invisible against the light card");
    }

    [Fact]
    public void A_dark_calendar_on_a_light_page_keeps_its_own_colour_as_the_edge()
    {
        var swatch = CalendarSwatch.For("#0D1429", LightSurface, LightForeground);

        swatch.Edge.Should().Be(swatch.Fill);
        swatch.Mark.Should().Be(Rgb.White);
    }

    [Fact]
    public void A_light_calendar_on_a_dark_page_keeps_its_own_colour_as_the_edge()
    {
        var swatch = CalendarSwatch.For("#FFFF00", DarkSurface, DarkForeground);

        swatch.Edge.Should().Be(swatch.Fill);
        swatch.Mark.ToHex().Should().Be("#101010");
    }

    [Fact]
    public void A_mid_colour_with_enough_contrast_to_the_page_is_its_own_edge()
    {
        var swatch = CalendarSwatch.For("#0B8043", LightSurface, LightForeground);

        ColorContrast.Ratio(swatch.Fill, LightSurface).Should().BeGreaterThanOrEqualTo(3);
        swatch.Edge.Should().Be(swatch.Fill);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#12")]
    [InlineData("blue")]
    [InlineData("#GG0000")]
    public void A_missing_or_malformed_colour_draws_in_the_theme_foreground(string? hex)
    {
        foreach (var dark in new[] { false, true })
        {
            var (surface, foreground) = Theme(dark);

            var swatch = CalendarSwatch.For(hex, surface, foreground);

            swatch.UsesCalendarColor.Should().BeFalse();
            swatch.Fill.Should().Be(foreground);
            swatch.Edge.Should().Be(foreground);
            ColorContrast.Ratio(swatch.Mark, swatch.Fill).Should().BeGreaterThanOrEqualTo(ColorContrast.TextMinimum);
        }
    }

    [Fact]
    public void The_two_themes_foregrounds_used_here_read_on_their_pages()
    {
        // The tests above rely on it: a foreground edge is only a good edge if it reads on the surface.
        ColorContrast.Ratio(LightForeground, LightSurface).Should().BeGreaterThanOrEqualTo(ColorContrast.GraphicMinimum);
        ColorContrast.Ratio(DarkForeground, DarkSurface).Should().BeGreaterThanOrEqualTo(ColorContrast.GraphicMinimum);
    }

    // ---- the colour bar on a card ----------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("#FBFBFB", false, false)]   // as light as the light card
    [InlineData("#FFFFFF", false, false)]
    [InlineData("#FFFF00", false, false)]   // yellow on white is 1.1 to 1
    [InlineData("#2B2B2B", true, false)]    // the dark card itself
    [InlineData("#0B0F19", true, false)]    // very dark navy on the dark card is 1.35 to 1
    [InlineData("#000000", true, false)]
    [InlineData("#34A853", false, true)]    // Google green on white (3.0 to 1 or a little under): a bar anyone can see
    [InlineData("#E8710A", false, true)]
    [InlineData("#4285F4", true, true)]
    [InlineData("#0B0F19", false, true)]    // very dark on the light card
    [InlineData("#FBFBFB", true, true)]     // very light on the dark card
    public void A_bar_needs_an_edge_only_when_its_colour_hardly_differs_from_the_card(string hex, bool dark, bool shows)
    {
        var (surface, foreground) = Theme(dark);

        var swatch = CalendarSwatch.For(hex, surface, foreground);

        swatch.FillShows.Should().Be(shows);
        (ColorContrast.Ratio(swatch.Fill, surface) >= ColorContrast.BarMinimum).Should().Be(shows);
    }

    [Fact]
    public void A_missing_colour_shows_in_the_foreground()
    {
        CalendarSwatch.For(null, LightSurface, LightForeground).FillShows.Should().BeTrue();
    }
}
