using System.Collections.Concurrent;
using TimeTug.Presentation.Tray;

namespace TimeTug.Platform.Tray;

/// <summary>
/// Draws the tray icon: the quiet, colour or attention puppy with the minute digit (1 to 9) centred on top, in white with a dark
/// outline so it reads on the dark puppy and on its white clock face, at 16, 20, 24 and 32 px (the notification-area sizes at 100 to
/// 200 percent scaling). Everything is computed here from the committed icon files and the bundled digit strokes
/// (<see cref="TrayGlyphSet"/>): no system font and no GDI, so the picture is the same on every machine. Pictures are cached per
/// (state, digit, taskbar theme, size) and the cache is safe to use from several threads.
/// <para>
/// A digit that is not 1 to 9 (0 and 10 or more cannot be shown in one digit) gives the plain icon, and never throws. The digit
/// colours are the same on a light and a dark taskbar because the digit sits on the colour puppy, which is dark, not on the
/// taskbar (a dark digit with a white outline was tried and read worse); the theme chooses the quiet puppy for the idle icon and
/// is part of the cache key.
/// </para>
/// </summary>
public sealed class TrayIconRenderer
{
    private const int Samples = 8; // 8 x 8 sub-samples per pixel for the edge coverage
    private static readonly int[] Supported = [16, 20, 24, 32];
    private static readonly (double R, double G, double B) Fill = (255, 255, 255);
    private static readonly (double R, double G, double B) Ring = (8, 12, 28);

    private readonly ITrayBaseIcons _baseIcons;
    private readonly TrayGlyphSet _glyphs;
    // PublicationOnly: a picture that failed to load (a missing file) is not remembered, the next call tries again.
    private readonly ConcurrentDictionary<(TrayIconState State, int? Digit, bool Light, int Size), Lazy<TrayIconImage>> _cache = new();
    private readonly ConcurrentDictionary<(int Digit, int Size), Lazy<GlyphCoverage>> _masks = new();

    public TrayIconRenderer(ITrayBaseIcons baseIcons, TrayGlyphSet? glyphs = null)
    {
        ArgumentNullException.ThrowIfNull(baseIcons);
        _baseIcons = baseIcons;
        _glyphs = glyphs ?? TrayGlyphSet.Default;
    }

    /// <summary>The picture sizes the icon files hold.</summary>
    public static IReadOnlyList<int> SupportedSizes => Supported;

    /// <summary>The supported size closest to <paramref name="sizePx"/>; a tie goes to the larger picture.</summary>
    public static int NearestSize(int sizePx)
    {
        var best = Supported[0];
        foreach (var size in Supported)
        {
            if (Math.Abs(size - sizePx) <= Math.Abs(best - sizePx))
            {
                best = size;
            }
        }

        return best;
    }

    /// <summary>
    /// The icon for <paramref name="state"/> with <paramref name="digit"/> drawn on it. The same instance comes back for the same
    /// arguments. <paramref name="sizePx"/> must be one of <see cref="SupportedSizes"/>.
    /// </summary>
    public TrayIconImage Render(TrayIconState state, int? digit, bool lightTaskbar, int sizePx)
    {
        if (!Supported.Contains(sizePx))
        {
            throw new ArgumentOutOfRangeException(nameof(sizePx), sizePx, "the icon files hold 16, 20, 24 and 32 px pictures");
        }

        int? shown = digit is >= 1 and <= 9 ? digit : null;
        return _cache.GetOrAdd((state, shown, lightTaskbar, sizePx), key => new Lazy<TrayIconImage>(() => Draw(key.State, key.Digit, key.Light, key.Size), LazyThreadSafetyMode.PublicationOnly)).Value;
    }

    private TrayIconImage Draw(TrayIconState state, int? digit, bool light, int size)
    {
        var plain = _baseIcons.Load(new TrayIconView(state, light).Variant, size);
        if (digit is not { } d)
        {
            return plain;
        }

        var mask = _masks.GetOrAdd((d, size), key => new Lazy<GlyphCoverage>(() => Rasterize(key.Digit, key.Size), LazyThreadSafetyMode.PublicationOnly)).Value;
        var rgba = plain.Rgba.ToArray();
        for (var i = 0; i < size * size; i++)
        {
            var ring = mask.Ring[i];
            if (ring == 0)
            {
                continue;
            }

            var pixel = rgba.AsSpan(i * 4, 4);
            Over(pixel, Ring, ring / (double)(Samples * Samples));
            Over(pixel, Fill, mask.Fill[i] / (double)(Samples * Samples));
        }

        return new TrayIconImage(size, size, rgba);
    }

    /// <summary>Source-over of a colour with coverage <paramref name="cover"/> on a straight-alpha pixel.</summary>
    private static void Over(Span<byte> pixel, (double R, double G, double B) colour, double cover)
    {
        if (cover <= 0)
        {
            return;
        }

        var backAlpha = pixel[3] / 255.0;
        var alpha = cover + (backAlpha * (1 - cover));
        pixel[0] = Channel(((colour.R * cover) + (pixel[0] * backAlpha * (1 - cover))) / alpha);
        pixel[1] = Channel(((colour.G * cover) + (pixel[1] * backAlpha * (1 - cover))) / alpha);
        pixel[2] = Channel(((colour.B * cover) + (pixel[2] * backAlpha * (1 - cover))) / alpha);
        pixel[3] = Channel(alpha * 255);
    }

    private static byte Channel(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    /// <summary>
    /// Which sub-samples of each pixel lie within half a stroke of the digit's lines (fill) or within half a stroke plus the outline
    /// (ring, which includes the fill). The grid points of the glyph file are spread over the glyph box with the stroke edges
    /// inside it, and the box is centred in the icon.
    /// </summary>
    private GlyphCoverage Rasterize(int digit, int size)
    {
        var m = _glyphs.MetricsFor(size);
        var left = (size - m.BoxWidth) / 2;
        var top = (size - m.BoxHeight) / 2;
        var half = m.Stroke / 2;
        double X(int column) => left + half + ((m.BoxWidth - m.Stroke) * column / (TrayGlyphSet.Columns - 1));
        double Y(int row) => top + half + ((m.BoxHeight - m.Stroke) * row / (TrayGlyphSet.Rows - 1));

        var segments = new List<(double Ax, double Ay, double Bx, double By)>();
        foreach (var stroke in _glyphs.StrokesFor(digit))
        {
            for (var i = 1; i < stroke.Count; i++)
            {
                segments.Add((X(stroke[i - 1].Column), Y(stroke[i - 1].Row), X(stroke[i].Column), Y(stroke[i].Row)));
            }
        }

        var fillLimit = half * half;
        var ringLimit = (half + m.Outline) * (half + m.Outline);
        var fill = new byte[size * size];
        var ring = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                // Pixels far from the glyph box cannot be covered: skip them before the sub-sampling.
                if (x < left - m.Outline || x > left + m.BoxWidth + m.Outline || y < top - m.Outline || y > top + m.BoxHeight + m.Outline)
                {
                    continue;
                }

                var inFill = 0;
                var inRing = 0;
                for (var sy = 0; sy < Samples; sy++)
                {
                    for (var sx = 0; sx < Samples; sx++)
                    {
                        var px = x + ((sx + 0.5) / Samples);
                        var py = y + ((sy + 0.5) / Samples);
                        var nearest = double.MaxValue;
                        foreach (var (ax, ay, bx, by) in segments)
                        {
                            nearest = Math.Min(nearest, DistanceSquared(px, py, ax, ay, bx, by));
                        }

                        if (nearest <= fillLimit)
                        {
                            inFill++;
                            inRing++;
                        }
                        else if (nearest <= ringLimit)
                        {
                            inRing++;
                        }
                    }
                }

                fill[(y * size) + x] = (byte)inFill;
                ring[(y * size) + x] = (byte)inRing;
            }
        }

        return new GlyphCoverage(fill, ring);
    }

    private static double DistanceSquared(double px, double py, double ax, double ay, double bx, double by)
    {
        var dx = bx - ax;
        var dy = by - ay;
        var length = (dx * dx) + (dy * dy);
        var t = length == 0 ? 0 : Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / length, 0, 1);
        var cx = px - (ax + (t * dx));
        var cy = py - (ay + (t * dy));
        return (cx * cx) + (cy * cy);
    }

    private sealed record GlyphCoverage(byte[] Fill, byte[] Ring);
}
