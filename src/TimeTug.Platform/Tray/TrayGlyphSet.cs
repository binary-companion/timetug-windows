using System.Globalization;

namespace TimeTug.Platform.Tray;

/// <summary>How one icon size draws a digit: the box the digit fills (stroke edges included), the stroke width and the outline width, all in pixels.</summary>
public sealed record GlyphMetrics(int Size, int BoxWidth, int BoxHeight, double Stroke, int Outline);

/// <summary>
/// The bundled digits 1 to 9: original strokes committed as <c>Assets/Tray/Glyphs/digits.txt</c> (see the README beside it) and
/// embedded in this assembly, so no system font is involved and the picture is the same on every machine. A digit is a few
/// polylines on a grid of 5 columns and 7 rows; <see cref="MetricsFor"/> says how that grid is scaled for an icon size.
/// </summary>
public sealed class TrayGlyphSet
{
    public const int Columns = 5;
    public const int Rows = 7;
    private const string ResourceName = "TimeTug.Platform.Tray.digits.txt";
    private static readonly Lazy<TrayGlyphSet> Bundled = new(LoadBundled);

    private readonly SortedDictionary<int, GlyphMetrics> _sizes;
    private readonly IReadOnlyList<IReadOnlyList<(int Column, int Row)>>[] _digits;

    private TrayGlyphSet(SortedDictionary<int, GlyphMetrics> sizes, IReadOnlyList<IReadOnlyList<(int Column, int Row)>>[] digits)
    {
        _sizes = sizes;
        _digits = digits;
    }

    /// <summary>The set committed with the app.</summary>
    public static TrayGlyphSet Default => Bundled.Value;

    /// <summary>The icon sizes this set has metrics for, ascending.</summary>
    public IReadOnlyList<int> Sizes => [.. _sizes.Keys];

    /// <summary>Reads the glyph file format; a malformed file is a <see cref="FormatException"/> naming the line.</summary>
    public static TrayGlyphSet Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var sizes = new SortedDictionary<int, GlyphMetrics>();
        var digits = new List<List<(int Column, int Row)>>?[10];
        List<List<(int Column, int Row)>>? current = null;
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (words[0])
            {
                case "size":
                    var metrics = ParseSize(words, number);
                    if (!sizes.TryAdd(metrics.Size, metrics))
                    {
                        throw Bad(number, $"size {metrics.Size} twice");
                    }

                    break;
                case "digit":
                    if (words.Length != 2 || !int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out var digit) || digit is < 1 or > 9)
                    {
                        throw Bad(number, "a digit line is 'digit 1' to 'digit 9'");
                    }

                    if (digits[digit] is not null)
                    {
                        throw Bad(number, $"digit {digit} twice");
                    }

                    current = [];
                    digits[digit] = current;
                    break;
                case "line":
                    if (current is null)
                    {
                        throw Bad(number, "a line belongs after a digit");
                    }

                    current.Add(ParseLine(words, number));
                    break;
                default:
                    throw Bad(number, $"unknown word '{words[0]}'");
            }
        }

        if (sizes.Count == 0)
        {
            throw new FormatException("the glyph file has no size line");
        }

        var all = new IReadOnlyList<IReadOnlyList<(int Column, int Row)>>[10];
        for (var d = 1; d <= 9; d++)
        {
            if (digits[d] is not { Count: > 0 } strokes)
            {
                throw new FormatException($"the glyph file has no strokes for digit {d}");
            }

            all[d] = [.. strokes.Select(s => (IReadOnlyList<(int Column, int Row)>)s.AsReadOnly())];
        }

        return new TrayGlyphSet(sizes, all);
    }

    public GlyphMetrics MetricsFor(int size) =>
        _sizes.TryGetValue(size, out var metrics) ? metrics : throw new ArgumentOutOfRangeException(nameof(size), size, "no digits are drawn at this icon size");

    /// <summary>The polylines of <paramref name="digit"/> (1 to 9) as grid points.</summary>
    public IReadOnlyList<IReadOnlyList<(int Column, int Row)>> StrokesFor(int digit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(digit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(digit, 9);
        return _digits[digit];
    }

    private static TrayGlyphSet LoadBundled()
    {
        using var stream = typeof(TrayGlyphSet).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("the digit glyph file is not embedded in the platform assembly");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    // size 16 box 5 7 stroke 1 outline 1
    private static GlyphMetrics ParseSize(string[] words, int number)
    {
        if (words.Length != 9 || words[2] != "box" || words[5] != "stroke" || words[7] != "outline")
        {
            throw Bad(number, "a size line is 'size N box W H stroke S outline O'");
        }

        var culture = CultureInfo.InvariantCulture;
        if (!int.TryParse(words[1], NumberStyles.None, culture, out var size)
            || !int.TryParse(words[3], NumberStyles.None, culture, out var width)
            || !int.TryParse(words[4], NumberStyles.None, culture, out var height)
            || !double.TryParse(words[6], NumberStyles.Float, culture, out var stroke)
            || !int.TryParse(words[8], NumberStyles.None, culture, out var outline)
            || size <= 0 || width <= 0 || height <= 0 || stroke <= 0 || stroke > Math.Min(width, height) / 2.0)
        {
            throw Bad(number, "the numbers of the size line are not valid");
        }

        if ((size - width) / 2 < outline || (size - height) / 2 < outline)
        {
            throw Bad(number, "the digit and its outline do not fit in the icon");
        }

        return new GlyphMetrics(size, width, height, stroke, outline);
    }

    // line 1,1 2,0 2,6
    private static List<(int Column, int Row)> ParseLine(string[] words, int number)
    {
        if (words.Length < 3)
        {
            throw Bad(number, "a stroke needs at least two points");
        }

        var points = new List<(int Column, int Row)>();
        foreach (var word in words.Skip(1))
        {
            var parts = word.Split(',');
            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var column)
                || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var row)
                || column >= Columns || row >= Rows)
            {
                throw Bad(number, $"'{word}' is not a point on the {Columns} by {Rows} grid (column,row)");
            }

            points.Add((column, row));
        }

        return points;
    }

    private static FormatException Bad(int line, string problem) => new($"digits file, line {line}: {problem}");
}
