using TimeTug.Presentation.Theming;

namespace TimeTug.Presentation.Tests.Theming;

public class ColorContrastTests
{
    [Fact]
    public void Black_and_white_are_the_extremes_of_luminance_and_21_to_1_apart()
    {
        ColorContrast.RelativeLuminance(Rgb.Black).Should().Be(0);
        ColorContrast.RelativeLuminance(Rgb.White).Should().BeApproximately(1, 1e-9);
        ColorContrast.Ratio(Rgb.White, Rgb.Black).Should().BeApproximately(21, 1e-9);
        ColorContrast.Ratio(Rgb.Black, Rgb.White).Should().BeApproximately(21, 1e-9);
        ColorContrast.Ratio(Rgb.White, Rgb.White).Should().BeApproximately(1, 1e-9);
    }

    [Fact]
    public void Luminance_follows_the_wcag_formula()
    {
        // Pure channels weigh 0.2126, 0.7152 and 0.0722; #767676 is the well-known grey at 4.54 to 1 against white.
        ColorContrast.RelativeLuminance(new Rgb(255, 0, 0)).Should().BeApproximately(0.2126, 1e-6);
        ColorContrast.RelativeLuminance(new Rgb(0, 255, 0)).Should().BeApproximately(0.7152, 1e-6);
        ColorContrast.RelativeLuminance(new Rgb(0, 0, 255)).Should().BeApproximately(0.0722, 1e-6);
        ColorContrast.Ratio(Rgb.White, new Rgb(0x76, 0x76, 0x76)).Should().BeApproximately(4.54, 0.01);
        // The linear segment of the sRGB curve for very dark channels.
        ColorContrast.RelativeLuminance(new Rgb(10, 10, 10)).Should().BeApproximately(10 / 255.0 / 12.92, 1e-9);
    }

    [Theory]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#FFFFFF", 255, 255, 255)]
    [InlineData("#4285f4", 0x42, 0x85, 0xF4)]
    [InlineData("#0B8043", 0x0B, 0x80, 0x43)]
    public void A_hex_colour_is_read_in_either_case(string hex, int r, int g, int b)
    {
        Rgb.TryParseHex(hex, out var colour).Should().BeTrue();
        colour.Should().Be(new Rgb((byte)r, (byte)g, (byte)b));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#fff")]
    [InlineData("000000")]
    [InlineData("#00000")]
    [InlineData("#0000000")]
    [InlineData("#GGGGGG")]
    [InlineData("#-12345")]
    [InlineData("#+12345")]
    [InlineData("#12 456")]
    [InlineData("red")]
    public void Anything_but_hash_and_six_hex_digits_is_not_a_colour(string? text)
    {
        Rgb.TryParseHex(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Hex_round_trips_in_upper_case()
    {
        new Rgb(0x0B, 0x80, 0x43).ToHex().Should().Be("#0B8043");
        Rgb.TryParseHex("#e67c73", out var colour).Should().BeTrue();
        colour.ToHex().Should().Be("#E67C73");
    }

    [Fact]
    public void A_translucent_colour_is_mixed_with_what_is_behind_it()
    {
        Rgb.White.Over(new Rgb(0x20, 0x20, 0x20), 0).Should().Be(new Rgb(0x20, 0x20, 0x20));
        Rgb.White.Over(new Rgb(0x20, 0x20, 0x20), 255).Should().Be(Rgb.White);
        // WinUI's dark card fill: white at 5% (0x0D) over #202020 is #2B2B2B.
        Rgb.White.Over(new Rgb(0x20, 0x20, 0x20), 0x0D).Should().Be(new Rgb(0x2B, 0x2B, 0x2B));
    }

    // ---- the mark on a fill ---------------------------------------------------------------------------------------------------------

    public static TheoryData<string, string> Marks => new()
    {
        { "#000000", "#FFFFFF" },   // pure black
        { "#0A0A0A", "#FFFFFF" },   // near-black
        { "#0D1429", "#FFFFFF" },   // dark navy (the brand navy)
        { "#1A237E", "#FFFFFF" },   // indigo
        { "#FFFF00", "#101010" },   // yellow
        { "#FFFFFF", "#101010" },   // white
        { "#FAFAFA", "#101010" },   // near-white
    };

    [Theory]
    [MemberData(nameof(Marks))]
    public void The_mark_is_white_on_dark_fills_and_near_black_on_light_ones(string fillHex, string markHex)
    {
        Rgb.TryParseHex(fillHex, out var fill).Should().BeTrue();

        var mark = ColorContrast.ReadableOn(fill);

        mark.ToHex().Should().Be(markHex);
        ColorContrast.Ratio(mark, fill).Should().BeGreaterThanOrEqualTo(ColorContrast.TextMinimum);
    }

    [Fact]
    public void A_mid_grey_gets_a_mark_that_still_reads_at_4_5_to_1()
    {
        var grey = new Rgb(0x80, 0x80, 0x80);

        var mark = ColorContrast.ReadableOn(grey);

        // #808080 is 3.95 to 1 against white, so white is out; near-black reaches 4.8 to 1.
        mark.ToHex().Should().Be("#101010");
        ColorContrast.Ratio(mark, grey).Should().BeGreaterThanOrEqualTo(ColorContrast.TextMinimum);
    }

    [Fact]
    public void Near_black_is_not_enough_for_a_fill_just_past_the_luminance_boundary_so_black_is_used()
    {
        // Fills whose luminance is between 0.1833 (white falls under 4.5) and about 0.198 (near-black #101010 is still under 4.5).
        var fill = Enumerable.Range(0, 256).Select(v => new Rgb((byte)v, (byte)v, (byte)v))
            .First(c => ColorContrast.Ratio(Rgb.White, c) < ColorContrast.TextMinimum && ColorContrast.Ratio(new Rgb(0x10, 0x10, 0x10), c) < ColorContrast.TextMinimum);

        var mark = ColorContrast.ReadableOn(fill);

        mark.Should().Be(Rgb.Black);
        ColorContrast.Ratio(mark, fill).Should().BeGreaterThanOrEqualTo(ColorContrast.TextMinimum);
    }

    [Fact]
    public void The_luminance_boundary_for_a_white_mark_sits_between_the_greys_76_and_77_hex()
    {
        // #767676 is 4.54 to 1 against white (kept), #777777 is 4.48 (not enough).
        var kept = new Rgb(0x76, 0x76, 0x76);
        var lost = new Rgb(0x77, 0x77, 0x77);

        ColorContrast.Ratio(Rgb.White, kept).Should().BeGreaterThanOrEqualTo(4.5);
        ColorContrast.Ratio(Rgb.White, lost).Should().BeLessThan(4.5);
        ColorContrast.ReadableOn(kept).Should().Be(Rgb.White);
        ColorContrast.ReadableOn(lost).Should().NotBe(Rgb.White);
        ColorContrast.Ratio(ColorContrast.ReadableOn(lost), lost).Should().BeGreaterThanOrEqualTo(4.5);
    }

    [Fact]
    public void Every_grey_gets_a_mark_at_4_5_to_1_or_better()
    {
        for (var v = 0; v <= 255; v++)
        {
            var fill = new Rgb((byte)v, (byte)v, (byte)v);
            ColorContrast.Ratio(ColorContrast.ReadableOn(fill), fill).Should().BeGreaterThanOrEqualTo(ColorContrast.TextMinimum, $"grey {v}");
        }
    }

    [Fact]
    public void Every_sampled_colour_gets_a_mark_at_4_5_to_1_or_better()
    {
        for (var r = 0; r <= 255; r += 15)
        {
            for (var g = 0; g <= 255; g += 15)
            {
                for (var b = 0; b <= 255; b += 15)
                {
                    var fill = new Rgb((byte)r, (byte)g, (byte)b);
                    ColorContrast.Ratio(ColorContrast.ReadableOn(fill), fill).Should().BeGreaterThanOrEqualTo(ColorContrast.TextMinimum, fill.ToHex());
                }
            }
        }
    }
}
