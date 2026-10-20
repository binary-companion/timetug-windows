namespace TimeTug.Platform.Tray;

/// <summary>
/// One picture of every digit at every size, enlarged by whole pixels (no smoothing) on a dark and on a light taskbar colour, for
/// looking at legibility and for <c>docs/screenshots</c>. Nine columns (digits 1 to 9); a dark band of four rows (16, 20, 24, 32 px)
/// above a light band of four rows.
/// </summary>
public static class TrayContactSheet
{
    public const int Scale = 4;
    private const int Margin = 8;
    private const int Cell = (32 * Scale) + Margin;
    private static readonly int[] Sizes = [16, 20, 24, 32];
    private static readonly (byte R, byte G, byte B) Dark = (32, 32, 32);
    private static readonly (byte R, byte G, byte B) Light = (243, 243, 243);

    public static int Width => Margin + (9 * Cell);

    public static int Height => Margin + (8 * Cell);

    /// <summary>The top left corner, in sheet pixels, of the enlarged icon of <paramref name="digit"/> at <paramref name="size"/> on a light or dark taskbar.</summary>
    public static (int X, int Y) CellOrigin(int digit, int size, bool light)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digit, 9);
        var row = Array.IndexOf(Sizes, size);
        ArgumentOutOfRangeException.ThrowIfNegative(row, nameof(size));
        return (Margin + ((digit - 1) * Cell), Margin + (((light ? 4 : 0) + row) * Cell));
    }

    public static TrayIconImage Build(TrayIconRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        var width = Width;
        var rgba = new byte[width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            var (r, g, b) = y < Margin + (4 * Cell) ? Dark : Light;
            for (var x = 0; x < width; x++)
            {
                var i = ((y * width) + x) * 4;
                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = b;
                rgba[i + 3] = 255;
            }
        }

        foreach (var light in new[] { false, true })
        {
            var background = light ? Light : Dark;
            foreach (var size in Sizes)
            {
                for (var digit = 1; digit <= 9; digit++)
                {
                    var icon = renderer.Render(Presentation.Tray.TrayIconState.Soon, digit, light, size);
                    var (ox, oy) = CellOrigin(digit, size, light);
                    for (var py = 0; py < size; py++)
                    {
                        for (var px = 0; px < size; px++)
                        {
                            var (r, g, b, a) = icon.PixelAt(px, py);
                            var mixed = (R: Blend(r, background.R, a), G: Blend(g, background.G, a), B: Blend(b, background.B, a));
                            for (var dy = 0; dy < Scale; dy++)
                            {
                                for (var dx = 0; dx < Scale; dx++)
                                {
                                    var i = (((oy + (py * Scale) + dy) * width) + ox + (px * Scale) + dx) * 4;
                                    rgba[i] = mixed.R;
                                    rgba[i + 1] = mixed.G;
                                    rgba[i + 2] = mixed.B;
                                }
                            }
                        }
                    }
                }
            }
        }

        return new TrayIconImage(width, Height, rgba);
    }

    private static byte Blend(byte colour, byte background, byte alpha) =>
        (byte)(((colour * alpha) + (background * (255 - alpha)) + 127) / 255);
}
