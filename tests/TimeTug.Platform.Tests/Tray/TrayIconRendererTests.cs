using TimeTug.Platform.Tray;
using TimeTug.Presentation.Tray;

namespace TimeTug.Platform.Tests.Tray;

public class TrayIconRendererTests
{
    private static readonly int[] Sizes = [16, 20, 24, 32];

    private static TrayIconImage Base(string variant, int size) => TrayTestAssets.Icons().Load(variant, size);

    [Fact]
    public void The_four_icon_files_load_at_every_size_with_the_size_asked_for()
    {
        foreach (var variant in new[] { "light", "dark", "color", "attention" })
        {
            foreach (var size in Sizes)
            {
                var image = Base(variant, size);
                image.Width.Should().Be(size, $"{variant} {size}");
                image.Height.Should().Be(size);
                image.Rgba.ToArray().Any(b => b != 0).Should().BeTrue($"{variant} {size} is not empty");
            }
        }
    }

    [Fact]
    public void A_size_the_file_does_not_hold_is_an_error()
    {
        var act = () => Base("color", 18);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_missing_icon_file_is_an_error_that_names_it()
    {
        var act = () => new TrayIconFiles(Path.Combine(TrayTestAssets.IconFolder, "nowhere")).Load("color", 16);
        act.Should().Throw<IOException>();
    }

    [Theory]
    [InlineData(TrayIconState.Idle, false, "dark")]
    [InlineData(TrayIconState.Idle, true, "light")]
    [InlineData(TrayIconState.Soon, false, "color")]
    [InlineData(TrayIconState.Soon, true, "color")]
    [InlineData(TrayIconState.Attention, true, "attention")]
    public void Without_a_digit_the_icon_is_the_plain_file_picture(TrayIconState state, bool light, string variant)
    {
        var renderer = TrayTestAssets.Renderer();
        foreach (var size in Sizes)
        {
            renderer.Render(state, null, light, size).HasSamePixels(Base(variant, size)).Should().BeTrue($"{state} {light} {size}");
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(-3)]
    [InlineData(99)]
    public void A_digit_that_is_not_one_to_nine_gives_the_plain_icon_and_never_throws(int digit)
    {
        var renderer = TrayTestAssets.Renderer();
        foreach (var size in Sizes)
        {
            TrayIconImage image = null!;
            var act = () => image = renderer.Render(TrayIconState.Soon, digit, false, size);
            act.Should().NotThrow();
            image.HasSamePixels(Base("color", size)).Should().BeTrue($"digit {digit} at {size}");
        }
    }

    [Fact]
    public void Only_the_sizes_the_files_hold_are_drawn_and_other_sizes_map_to_the_nearest()
    {
        TrayIconRenderer.SupportedSizes.Should().Equal(16, 20, 24, 32);
        TrayIconRenderer.NearestSize(16).Should().Be(16);
        TrayIconRenderer.NearestSize(18).Should().Be(20, "ties go to the larger picture, which is easier to read");
        TrayIconRenderer.NearestSize(17).Should().Be(16);
        TrayIconRenderer.NearestSize(22).Should().Be(24);
        TrayIconRenderer.NearestSize(48).Should().Be(32);
        TrayIconRenderer.NearestSize(8).Should().Be(16);
        TrayIconRenderer.NearestSize(0).Should().Be(16);

        var act = () => TrayTestAssets.Renderer().Render(TrayIconState.Soon, 5, false, 18);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Every_digit_changes_the_picture_and_no_two_digits_look_alike_at_any_size()
    {
        var renderer = TrayTestAssets.Renderer();
        foreach (var size in Sizes)
        {
            var plain = renderer.Render(TrayIconState.Soon, null, false, size);
            var images = Enumerable.Range(1, 9).Select(d => renderer.Render(TrayIconState.Soon, d, false, size)).ToList();
            foreach (var image in images)
            {
                Differences(image, plain).Should().BeGreaterThan(size, $"the digit is drawn at {size}");
            }

            for (var a = 0; a < 9; a++)
            {
                for (var b = a + 1; b < 9; b++)
                {
                    Differences(images[a], images[b]).Should().BeGreaterThanOrEqualTo(size / 4, $"digits {a + 1} and {b + 1} at {size} differ");
                }
            }
        }
    }

    [Fact]
    public void The_digit_stays_in_the_middle_and_leaves_the_edge_of_the_icon_alone()
    {
        var renderer = TrayTestAssets.Renderer();
        foreach (var size in Sizes)
        {
            var plain = Base("color", size);
            for (var digit = 1; digit <= 9; digit++)
            {
                var image = renderer.Render(TrayIconState.Soon, digit, false, size);
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var margin = size / 6;
                        if (x < margin || y < margin || x >= size - margin || y >= size - margin)
                        {
                            image.PixelAt(x, y).Should().Be(plain.PixelAt(x, y), $"digit {digit} at {size}: pixel {x},{y} is outside the digit");
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void The_digit_is_a_light_shape_with_a_dark_outline_that_reads_on_the_puppy()
    {
        var renderer = TrayTestAssets.Renderer();
        foreach (var size in Sizes)
        {
            for (var digit = 1; digit <= 9; digit++)
            {
                var image = renderer.Render(TrayIconState.Soon, digit, false, size);
                var plain = Base("color", size);
                var bright = CountInBox(image, p => p.A == 255 && p.R > 200 && p.G > 200 && p.B > 200);
                // The ring is new dark pixels around the strokes (the puppy is dark already, so only part of it shows as a change).
                var ring = CountChanged(image, plain, p => p.A == 255 && p.R < 30 && p.G < 34 && p.B < 54);
                ring.Should().BeGreaterThanOrEqualTo(size / 4, $"digit {digit} at {size} has a dark outline");
                bright.Should().BeGreaterThanOrEqualTo(size / 2, $"digit {digit} at {size} has enough light pixels");
            }
        }
    }

    [Fact]
    public void The_same_inputs_give_the_same_picture_on_a_second_renderer()
    {
        var a = TrayTestAssets.Renderer();
        var b = TrayTestAssets.Renderer();
        foreach (var size in Sizes)
        {
            for (var digit = 1; digit <= 9; digit++)
            {
                a.Render(TrayIconState.Soon, digit, true, size).HasSamePixels(b.Render(TrayIconState.Soon, digit, true, size)).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void The_digit_does_not_depend_on_the_taskbar_theme_because_it_sits_on_the_colour_puppy()
    {
        var renderer = TrayTestAssets.Renderer();
        foreach (var size in Sizes)
        {
            for (var digit = 1; digit <= 9; digit++)
            {
                renderer.Render(TrayIconState.Soon, digit, true, size).HasSamePixels(renderer.Render(TrayIconState.Soon, digit, false, size)).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void The_cache_returns_the_same_picture_for_the_same_key()
    {
        var renderer = TrayTestAssets.Renderer();
        var first = renderer.Render(TrayIconState.Soon, 5, false, 16);
        renderer.Render(TrayIconState.Soon, 5, false, 16).Should().BeSameAs(first);
        renderer.Render(TrayIconState.Soon, 4, false, 16).Should().NotBeSameAs(first);
        renderer.Render(TrayIconState.Soon, 5, false, 24).Should().NotBeSameAs(first);
        renderer.Render(TrayIconState.Soon, 5, true, 16).Should().NotBeSameAs(first, "the theme is part of the key");
    }

    [Fact]
    public void A_changed_theme_draws_the_other_quiet_icon()
    {
        var renderer = TrayTestAssets.Renderer();
        var dark = renderer.Render(TrayIconState.Idle, null, false, 16);
        var light = renderer.Render(TrayIconState.Idle, null, true, 16);
        light.Should().NotBeSameAs(dark);
        light.HasSamePixels(dark).Should().BeFalse();
        renderer.Render(TrayIconState.Idle, null, true, 16).Should().BeSameAs(light);
    }

    [Fact]
    public void Out_of_range_digits_share_the_plain_icons_cache_entry()
    {
        var renderer = TrayTestAssets.Renderer();
        renderer.Render(TrayIconState.Soon, 10, false, 20).Should().BeSameAs(renderer.Render(TrayIconState.Soon, null, false, 20));
        renderer.Render(TrayIconState.Soon, 0, false, 20).Should().BeSameAs(renderer.Render(TrayIconState.Soon, null, false, 20));
    }

    [Fact]
    public void Rendering_from_many_threads_gives_one_picture_per_key()
    {
        var renderer = TrayTestAssets.Renderer();
        var images = Enumerable.Range(0, 32).AsParallel().Select(_ => renderer.Render(TrayIconState.Soon, 7, false, 32)).ToList();
        images.Distinct().Count().Should().Be(1);
    }

    [Fact]
    public void A_picture_the_renderer_makes_survives_png_and_gives_a_loadable_icon_file()
    {
        var image = TrayTestAssets.Renderer().Render(TrayIconState.Soon, 3, false, 24);
        TrayIconImage.FromPng(image.ToPng()).HasSamePixels(image).Should().BeTrue();
        image.ToIco().Length.Should().Be(22 + 40 + 24 * 24 * 4 + 4 * 24);
    }

    [Fact]
    public void The_renderer_uses_the_glyph_set_it_is_given()
    {
        const string tiny = "size 16 box 5 7 stroke 1 outline 1\ndigit 1\n line 0,0 4,0\ndigit 2\n line 0,0 4,0\ndigit 3\n line 0,0 4,0\ndigit 4\n line 0,0 4,0\n" +
            "digit 5\n line 0,0 4,0\ndigit 6\n line 0,0 4,0\ndigit 7\n line 0,0 4,0\ndigit 8\n line 0,0 4,0\ndigit 9\n line 0,0 4,0\n";
        var renderer = new TrayIconRenderer(TrayTestAssets.Icons(), TrayGlyphSet.Parse(tiny));
        renderer.Render(TrayIconState.Soon, 1, false, 16).HasSamePixels(renderer.Render(TrayIconState.Soon, 9, false, 16)).Should().BeTrue("every digit is one bar in this set");
    }

    /// <summary>The pixels in the middle of the icon (where the digit goes) that satisfy <paramref name="test"/>, whether or not they changed.</summary>
    private static int CountInBox(TrayIconImage image, Func<(byte R, byte G, byte B, byte A), bool> test)
    {
        var n = 0;
        var margin = image.Width / 6;
        for (var y = margin; y < image.Height - margin; y++)
        {
            for (var x = margin; x < image.Width - margin; x++)
            {
                n += test(image.PixelAt(x, y)) ? 1 : 0;
            }
        }

        return n;
    }

    /// <summary>The pixels of <paramref name="image"/> that differ from <paramref name="plain"/> and satisfy <paramref name="test"/>.</summary>
    private static int CountChanged(TrayIconImage image, TrayIconImage plain, Func<(byte R, byte G, byte B, byte A), bool> test)
    {
        var n = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                n += image.PixelAt(x, y) != plain.PixelAt(x, y) && test(image.PixelAt(x, y)) ? 1 : 0;
            }
        }

        return n;
    }

    private static int Differences(TrayIconImage a, TrayIconImage b)
    {
        var n = 0;
        for (var y = 0; y < a.Height; y++)
        {
            for (var x = 0; x < a.Width; x++)
            {
                n += a.PixelAt(x, y) == b.PixelAt(x, y) ? 0 : 1;
            }
        }

        return n;
    }
}
