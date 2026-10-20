using TimeTug.Platform.Tray;

namespace TimeTug.Platform.Tests.Tray;

public class TrayContactSheetTests
{
    [Fact]
    public void The_sheet_shows_every_digit_at_every_size_enlarged_on_a_dark_and_a_light_taskbar()
    {
        var sheet = TrayContactSheet.Build(TrayTestAssets.Renderer());
        sheet.Width.Should().Be(TrayContactSheet.Width);
        sheet.Height.Should().Be(TrayContactSheet.Height);
        // 9 digits and 4 sizes in each of two theme bands; enlargement is a whole number so pixels stay square.
        TrayContactSheet.Scale.Should().BeGreaterThanOrEqualTo(4);
        sheet.ToPng().Length.Should().BeLessThan(150_000, "it is committed");
    }

    [Fact]
    public void The_sheet_is_the_renderer_output_scaled_up_without_smoothing()
    {
        var renderer = TrayTestAssets.Renderer();
        var sheet = TrayContactSheet.Build(renderer);
        var (x, y) = TrayContactSheet.CellOrigin(digit: 5, size: 32, light: false);
        var icon = renderer.Render(Presentation.Tray.TrayIconState.Soon, 5, false, 32);
        var checkedPixels = 0;
        for (var py = 0; py < 32; py += 5)
        {
            for (var px = 0; px < 32; px += 5)
            {
                var (r, g, b, a) = icon.PixelAt(px, py);
                if (a != 255)
                {
                    continue;
                }

                sheet.PixelAt(x + (px * TrayContactSheet.Scale) + 1, y + (py * TrayContactSheet.Scale) + 1).Should().Be((r, g, b, (byte)255));
                checkedPixels++;
            }
        }

        checkedPixels.Should().BeGreaterThan(10);
    }
}
