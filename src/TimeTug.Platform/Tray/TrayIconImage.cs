using System.Buffers.Binary;

namespace TimeTug.Platform.Tray;

/// <summary>An icon picture: straight-alpha RGBA pixels, top row first. Immutable.</summary>
public sealed class TrayIconImage
{
    private readonly byte[] _rgba;

    public TrayIconImage(int width, int height, byte[] rgba)
    {
        ArgumentNullException.ThrowIfNull(rgba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (rgba.Length != checked(width * height * 4))
        {
            throw new ArgumentException($"a {width} x {height} picture needs {width * height * 4} bytes, not {rgba.Length}", nameof(rgba));
        }

        Width = width;
        Height = height;
        _rgba = (byte[])rgba.Clone();
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Red, green, blue, alpha for every pixel, row by row from the top.</summary>
    public ReadOnlySpan<byte> Rgba => _rgba;

    public (byte R, byte G, byte B, byte A) PixelAt(int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(x, Width);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);
        var i = ((y * Width) + x) * 4;
        return (_rgba[i], _rgba[i + 1], _rgba[i + 2], _rgba[i + 3]);
    }

    public bool HasSamePixels(TrayIconImage other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Width == other.Width && Height == other.Height && _rgba.AsSpan().SequenceEqual(other._rgba);
    }

    public static TrayIconImage FromPng(ReadOnlySpan<byte> png)
    {
        var (width, height, rgba) = PngCodec.Decode(png);
        return new TrayIconImage(width, height, rgba);
    }

    public byte[] ToPng() => PngCodec.Encode(Width, Height, _rgba);

    /// <summary>
    /// A one-picture <c>.ico</c> file: a 32-bit bitmap with alpha (blue, green, red, alpha, bottom row first) and an empty 1-bit mask,
    /// the form every Windows version loads into an icon handle (<c>new Icon(stream)</c>).
    /// </summary>
    public byte[] ToIco()
    {
        if (Width > 256 || Height > 256)
        {
            throw new InvalidOperationException("an icon picture is at most 256 pixels");
        }

        var maskRow = (((Width + 31) / 32) * 4);
        var imageLength = 40 + (Width * Height * 4) + (maskRow * Height);
        var ico = new byte[22 + imageLength];
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(2), 1); // type: icon
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(4), 1); // one image
        ico[6] = (byte)(Width == 256 ? 0 : Width);
        ico[7] = (byte)(Height == 256 ? 0 : Height);
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(10), 1); // planes
        BinaryPrimitives.WriteUInt16LittleEndian(ico.AsSpan(12), 32); // bits per pixel
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(14), imageLength);
        BinaryPrimitives.WriteInt32LittleEndian(ico.AsSpan(18), 22);

        var header = ico.AsSpan(22);
        BinaryPrimitives.WriteInt32LittleEndian(header, 40);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], Height * 2); // the mask counts as half of the height
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], 32);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], (Width * Height * 4) + (maskRow * Height));

        var offset = 22 + 40;
        for (var y = Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = ((y * Width) + x) * 4;
                ico[offset++] = _rgba[i + 2];
                ico[offset++] = _rgba[i + 1];
                ico[offset++] = _rgba[i];
                ico[offset++] = _rgba[i + 3];
            }
        }

        return ico; // the mask rows stay zero: opaque, the alpha channel decides
    }
}
