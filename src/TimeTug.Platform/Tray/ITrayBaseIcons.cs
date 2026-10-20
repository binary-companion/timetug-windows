using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace TimeTug.Platform.Tray;

/// <summary>The icon pictures the digits are drawn on: <c>light</c>, <c>dark</c>, <c>color</c> and <c>attention</c>, at the sizes the notification area uses.</summary>
public interface ITrayBaseIcons
{
    /// <summary>The picture of <paramref name="variant"/> at exactly <paramref name="sizePx"/> pixels.</summary>
    TrayIconImage Load(string variant, int sizePx);
}

/// <summary>Reads <c>tray-&lt;variant&gt;.ico</c> files (a PNG picture per size inside) from a folder, once each.</summary>
public sealed class TrayIconFiles : ITrayBaseIcons
{
    private readonly string _folder;
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<int, TrayIconImage>> _files = new(StringComparer.Ordinal);

    public TrayIconFiles(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        _folder = folder;
    }

    public TrayIconImage Load(string variant, int sizePx)
    {
        ArgumentException.ThrowIfNullOrEmpty(variant);
        var pictures = _files.GetOrAdd(variant, ReadFile);
        return pictures.TryGetValue(sizePx, out var image)
            ? image
            : throw new ArgumentOutOfRangeException(nameof(sizePx), sizePx, $"tray-{variant}.ico has no {sizePx} px picture (it has {string.Join(", ", pictures.Keys)})");
    }

    private Dictionary<int, TrayIconImage> ReadFile(string variant)
    {
        var path = Path.Combine(_folder, $"tray-{variant}.ico");
        var data = File.ReadAllBytes(path);
        if (data.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) != 1)
        {
            throw new InvalidDataException($"{path} is not an icon file");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        var pictures = new Dictionary<int, TrayIconImage>();
        for (var i = 0; i < count; i++)
        {
            var entry = data.AsSpan(6 + (16 * i), 16);
            var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
            if (offset < 0 || length < 0 || offset + length > data.Length)
            {
                throw new InvalidDataException($"{path} is cut short");
            }

            var body = data.AsSpan(offset, length);
            if (!PngCodec.HasSignature(body))
            {
                throw new InvalidDataException($"{path}: only PNG pictures inside the icon file are read");
            }

            var image = TrayIconImage.FromPng(body);
            pictures[image.Width] = image;
        }

        return pictures;
    }
}
