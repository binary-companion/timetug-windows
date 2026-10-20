using TimeTug.Platform.Tray;

namespace TimeTug.Platform.Tests.Tray;

public class TrayGlyphSetTests
{
    [Fact]
    public void The_bundled_set_has_digits_one_to_nine_at_the_four_notification_area_sizes()
    {
        var set = TrayGlyphSet.Default;
        set.Sizes.Should().Equal(16, 20, 24, 32);
        for (var digit = 1; digit <= 9; digit++)
        {
            set.StrokesFor(digit).Should().NotBeEmpty($"digit {digit}");
        }
    }

    [Fact]
    public void Every_stroke_point_lies_on_the_grid_and_every_stroke_has_a_length()
    {
        for (var digit = 1; digit <= 9; digit++)
        {
            foreach (var stroke in TrayGlyphSet.Default.StrokesFor(digit))
            {
                stroke.Count.Should().BeGreaterThanOrEqualTo(2);
                stroke.All(p => p.Column >= 0 && p.Column <= 4 && p.Row >= 0 && p.Row <= 6).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Each_size_leaves_room_for_the_outline_inside_the_icon_and_grows_with_the_icon()
    {
        var previous = 0;
        foreach (var size in TrayGlyphSet.Default.Sizes)
        {
            var m = TrayGlyphSet.Default.MetricsFor(size);
            m.Size.Should().Be(size);
            ((size - m.BoxWidth) / 2).Should().BeGreaterThanOrEqualTo(m.Outline, "the outline stays inside the icon");
            ((size - m.BoxHeight) / 2).Should().BeGreaterThanOrEqualTo(m.Outline);
            m.Stroke.Should().BeGreaterThan(0);
            m.BoxHeight.Should().BeGreaterThan(previous);
            previous = m.BoxHeight;
        }
    }

    [Fact]
    public void An_unknown_size_has_no_metrics()
    {
        var act = () => TrayGlyphSet.Default.MetricsFor(18);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_digit_outside_one_to_nine_has_no_strokes()
    {
        var act = () => TrayGlyphSet.Default.StrokesFor(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private const string Valid = "size 16 box 5 7 stroke 1 outline 1\ndigit 1\n  line 1,1 2,0 2,6\n" +
        "digit 2\n line 0,0 4,0\ndigit 3\n line 0,0 4,0\ndigit 4\n line 0,0 4,0\ndigit 5\n line 0,0 4,0\n" +
        "digit 6\n line 0,0 4,0\ndigit 7\n line 0,0 4,0\ndigit 8\n line 0,0 4,0\ndigit 9\n line 0,0 4,0\n";

    [Fact]
    public void A_small_set_parses_and_comments_and_blank_lines_are_ignored()
    {
        var set = TrayGlyphSet.Parse("# comment\n\n" + Valid);
        set.Sizes.Should().Equal(16);
        set.StrokesFor(1).Single().Should().Equal((1, 1), (2, 0), (2, 6));
        set.MetricsFor(16).Should().Be(new GlyphMetrics(16, 5, 7, 1, 1));
    }

    [Theory]
    [InlineData("size 16 box 5 7 stroke 1\n")]
    [InlineData("digit 1\n  line 1,1 2,2\n")] // the same digit twice
    [InlineData("digit 1\n  line 9,9 2,2\n")]
    [InlineData("digit 1\n  line 1,1\n")]
    [InlineData("digit 10\n  line 1,1 2,2\n")]
    [InlineData("whatever 3\n")]
    public void A_malformed_file_is_refused(string text)
    {
        var act = () => TrayGlyphSet.Parse(Valid + text);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void A_line_before_any_digit_is_refused()
    {
        var act = () => TrayGlyphSet.Parse("size 16 box 5 7 stroke 1 outline 1\nline 1,1 2,2\n");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void A_set_without_every_digit_is_refused()
    {
        var act = () => TrayGlyphSet.Parse("size 16 box 5 7 stroke 1 outline 1\ndigit 1\n line 0,0 4,0\n");
        act.Should().Throw<FormatException>().WithMessage("*digit 2*");
    }

    [Fact]
    public void A_set_without_a_size_is_refused()
    {
        var act = () => TrayGlyphSet.Parse(Valid.Replace("size 16 box 5 7 stroke 1 outline 1\n", string.Empty, StringComparison.Ordinal));
        act.Should().Throw<FormatException>().WithMessage("*size*");
    }
}
