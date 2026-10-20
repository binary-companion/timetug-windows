using TimeTug.Platform.Tray;
using TimeTug.Presentation.Tray;

namespace TimeTug.Platform.Tests.Tray;

/// <summary>
/// Golden pictures of the digit icons (<c>tests/TimeTug.Platform.Tests/data/tray-digit-N-SIZE-THEME.png</c>), digits 1 to 9 at 16 and
/// 32 px for a light and a dark taskbar.
/// <para>
/// <b>Tolerance.</b> A pixel matches when every channel is within <see cref="Tolerance"/> (2 of 255) of the golden. The renderer
/// is pure managed code (the digit shapes are the bundled strokes, the base pictures are decoded from the committed icon
/// files, the compositing is arithmetic), so on one machine the result is exact. The tolerance is there for a different
/// runner image or architecture (x64, ARM64): floating-point rounding at the edge of a stroke could move a channel by one or
/// two steps, and that must not turn the build red. A real change (a moved stroke, a different colour, a different base
/// picture) differs by far more and still fails.
/// </para>
/// <para>
/// <b>Regenerating.</b> Goldens are never written by an ordinary test run. <c>scripts/ci/tray-goldens.ps1 -Update</c> sets
/// <c>TIMETUG_UPDATE_TRAY_GOLDENS=1</c>, which makes <see cref="Update_the_goldens_when_asked"/> rewrite them (and the contact sheet in
/// docs/screenshots) from the current renderer; review the pictures before committing.
/// </para>
/// </summary>
public class TrayGoldenTests
{
    public const int Tolerance = 2;
    private const string UpdateVariable = "TIMETUG_UPDATE_TRAY_GOLDENS";
    private static readonly int[] GoldenSizes = [16, 32];

    public static TheoryData<int, int, bool> Cases()
    {
        var data = new TheoryData<int, int, bool>();
        foreach (var size in GoldenSizes)
        {
            foreach (var light in new[] { false, true })
            {
                for (var digit = 1; digit <= 9; digit++)
                {
                    data.Add(digit, size, light);
                }
            }
        }

        return data;
    }

    private static string GoldenPath(int digit, int size, bool light) =>
        Path.Combine(TrayTestAssets.GoldenFolder(), FormattableString.Invariant($"tray-digit-{digit}-{size}-{(light ? "light" : "dark")}.png"));

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_digit_icon_matches_its_golden_picture_within_the_tolerance(int digit, int size, bool light)
    {
        var path = GoldenPath(digit, size, light);
        File.Exists(path).Should().BeTrue($"the golden {Path.GetFileName(path)} is committed (run scripts/ci/tray-goldens.ps1 -Update to create it)");
        var golden = TrayIconImage.FromPng(File.ReadAllBytes(path));
        var image = TrayTestAssets.Renderer().Render(TrayIconState.Soon, digit, light, size);

        image.Width.Should().Be(golden.Width);
        image.Height.Should().Be(golden.Height);
        var worst = MaxChannelDifference(image, golden);
        worst.Should().BeLessThanOrEqualTo(Tolerance, $"digit {digit} at {size} px (light taskbar: {light}) differs from its golden");
    }

    [Fact]
    public void The_comparison_flags_a_real_change_and_ignores_a_rounding_step()
    {
        var image = TrayTestAssets.Renderer().Render(TrayIconState.Soon, 5, false, 16);
        var rgba = image.Rgba.ToArray();
        var nudged = (byte[])rgba.Clone();
        nudged[40] = (byte)Math.Min(255, nudged[40] + Tolerance);
        MaxChannelDifference(new TrayIconImage(16, 16, nudged), image).Should().BeLessThanOrEqualTo(Tolerance);

        var moved = TrayTestAssets.Renderer().Render(TrayIconState.Soon, 6, false, 16);
        MaxChannelDifference(moved, image).Should().BeGreaterThan(Tolerance);
    }

    [Fact]
    public void Update_the_goldens_when_asked()
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) != "1")
        {
            return;
        }

        var renderer = TrayTestAssets.Renderer();
        Directory.CreateDirectory(TrayTestAssets.GoldenFolder());
        foreach (var size in GoldenSizes)
        {
            foreach (var light in new[] { false, true })
            {
                for (var digit = 1; digit <= 9; digit++)
                {
                    File.WriteAllBytes(GoldenPath(digit, size, light), renderer.Render(TrayIconState.Soon, digit, light, size).ToPng());
                }
            }
        }

        var sheet = TrayContactSheet.Build(renderer);
        File.WriteAllBytes(TrayTestAssets.ContactSheetPath(), sheet.ToPng());
    }

    internal static int MaxChannelDifference(TrayIconImage a, TrayIconImage b)
    {
        var worst = 0;
        var left = a.Rgba;
        var right = b.Rgba;
        for (var i = 0; i < left.Length; i++)
        {
            worst = Math.Max(worst, Math.Abs(left[i] - right[i]));
        }

        return worst;
    }
}
