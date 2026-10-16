using TimeTug.Platform.Capture;

namespace TimeTug.Platform.Tests;

public class PixelAnalysisTests
{
    private static byte[] Solid(int pixels, byte b, byte g, byte r)
    {
        var data = new byte[pixels * 4];
        for (var i = 0; i < pixels; i++)
        {
            data[i * 4] = b;
            data[(i * 4) + 1] = g;
            data[(i * 4) + 2] = r;
            data[(i * 4) + 3] = 255;
        }

        return data;
    }

    [Fact]
    public void A_solid_image_has_one_colour_and_is_blank()
    {
        var data = Solid(100, 10, 20, 30);

        PixelAnalysis.CountDistinctColours(data).Should().Be(1);
        PixelAnalysis.IsBlank(data).Should().BeTrue();
    }

    [Fact]
    public void An_image_with_one_different_pixel_is_not_blank()
    {
        var data = Solid(100, 10, 20, 30);
        data[40] = 200;

        PixelAnalysis.CountDistinctColours(data).Should().Be(2);
        PixelAnalysis.IsBlank(data).Should().BeFalse();
    }

    [Fact]
    public void An_empty_image_is_blank()
    {
        PixelAnalysis.IsBlank([]).Should().BeTrue();
        PixelAnalysis.CountDistinctColours([]).Should().Be(0);
    }

    [Fact]
    public void Counting_stops_at_the_limit()
    {
        var data = new byte[256 * 4];
        for (var i = 0; i < 256; i++)
        {
            data[i * 4] = (byte)i;
        }

        PixelAnalysis.CountDistinctColours(data, limit: 5).Should().Be(5);
        PixelAnalysis.CountDistinctColours(data).Should().Be(256);
    }

    [Fact]
    public void Trailing_bytes_that_do_not_make_a_whole_pixel_are_ignored()
    {
        PixelAnalysis.CountDistinctColours(new byte[] { 1, 2, 3, 4, 9, 9 }).Should().Be(1);
    }
}
