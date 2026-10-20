using TimeTug.Platform.Tray;

namespace TimeTug.Platform.Tests.Tray;

public class PngCodecTests
{
    private static TrayIconImage Sample()
    {
        // 3 x 2: opaque red, half-transparent green, transparent | blue, white, black.
        byte[] rgba =
        [
            255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 0, 0,
            0, 0, 255, 255, 255, 255, 255, 255, 0, 0, 0, 255,
        ];
        return new TrayIconImage(3, 2, rgba);
    }

    [Fact]
    public void A_picture_survives_a_round_trip_through_png()
    {
        var original = Sample();
        var back = TrayIconImage.FromPng(original.ToPng());
        back.Width.Should().Be(3);
        back.Height.Should().Be(2);
        back.Rgba.ToArray().Should().Equal(original.Rgba.ToArray());
    }

    [Fact]
    public void The_png_starts_with_the_png_signature()
    {
        Sample().ToPng().Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
    }

    [Fact]
    public void Something_that_is_not_a_png_is_refused()
    {
        var act = () => TrayIconImage.FromPng(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void A_picture_needs_the_right_number_of_pixels()
    {
        var act = () => new TrayIconImage(2, 2, new byte[15]);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_icon_file_has_the_header_one_entry_and_a_picture_of_the_right_size()
    {
        var image = Sample();
        var ico = image.ToIco();
        ico[..6].Should().Equal(0, 0, 1, 0, 1, 0); // reserved, type icon, one image
        ico[6].Should().Be(3); // width
        ico[7].Should().Be(2); // height
        BitConverter.ToUInt16(ico, 12).Should().Be(32); // bits per pixel
        var length = BitConverter.ToInt32(ico, 14);
        var offset = BitConverter.ToInt32(ico, 18);
        offset.Should().Be(22);
        ico.Length.Should().Be(offset + length);
        // BITMAPINFOHEADER (40) + BGRA pixels + 1-bit AND mask, 4-byte rows.
        length.Should().Be(40 + 3 * 2 * 4 + 4 * 2);
        BitConverter.ToInt32(ico, offset + 4).Should().Be(3);
        BitConverter.ToInt32(ico, offset + 8).Should().Be(4); // height doubled for the mask
    }

    [Fact]
    public void The_icon_file_stores_blue_green_red_alpha_with_the_bottom_row_first()
    {
        var ico = Sample().ToIco();
        // Bottom row of the picture first: blue, white, black.
        var pixels = 22 + 40;
        ico[pixels..(pixels + 4)].Should().Equal(255, 0, 0, 255); // blue as B,G,R,A
        ico[(pixels + 12)..(pixels + 16)].Should().Equal(0, 0, 255, 255); // top-left red
    }

    [Fact]
    public void The_pixel_accessor_reads_the_right_place()
    {
        Sample().PixelAt(1, 0).Should().Be(((byte)0, (byte)255, (byte)0, (byte)128));
        Sample().PixelAt(0, 1).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255));
    }
}
