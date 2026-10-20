using System.Buffers.Binary;
using System.IO.Compression;

namespace TimeTug.Platform.Tray;

/// <summary>
/// A small PNG reader and writer in managed code (8-bit RGB or RGBA, no interlacing), so the tray icon is made the same way on every
/// machine and needs no imaging library. The reader accepts what the icon files contain (any of the five row filters); the writer
/// picks a filter per row to keep the committed pictures small.
/// </summary>
internal static class PngCodec
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static bool HasSignature(ReadOnlySpan<byte> data) => data.Length >= Signature.Length && data[..Signature.Length].SequenceEqual(Signature);

    public static (int Width, int Height, byte[] Rgba) Decode(ReadOnlySpan<byte> png)
    {
        if (!HasSignature(png))
        {
            throw new InvalidDataException("not a PNG picture");
        }

        int width = 0, height = 0, channels = 0;
        using var compressed = new MemoryStream();
        var position = Signature.Length;
        var sawHeader = false;
        var sawEnd = false;
        while (position + 12 <= png.Length && !sawEnd)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[position..]);
            var type = png.Slice(position + 4, 4);
            if (length < 0 || position + 12 + length > png.Length)
            {
                throw new InvalidDataException("the PNG picture is cut short");
            }

            var body = png.Slice(position + 8, length);
            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                {
                    throw new InvalidDataException("bad PNG header");
                }

                width = (int)BinaryPrimitives.ReadUInt32BigEndian(body);
                height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                var depth = body[8];
                var colourType = body[9];
                var interlace = body[12];
                channels = colourType switch { 6 => 4, 2 => 3, _ => 0 };
                if (depth != 8 || channels == 0 || interlace != 0 || body[10] != 0 || body[11] != 0)
                {
                    throw new InvalidDataException("only 8-bit RGB and RGBA PNG pictures without interlacing are supported");
                }

                if (width <= 0 || height <= 0 || (long)width * height > 4096L * 4096L)
                {
                    throw new InvalidDataException("unreasonable PNG size");
                }

                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                compressed.Write(body);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                sawEnd = true;
            }

            position += 12 + length;
        }

        if (!sawHeader || compressed.Length == 0)
        {
            throw new InvalidDataException("the PNG picture has no image data");
        }

        var stride = width * channels;
        var raw = new byte[(stride + 1) * height];
        compressed.Position = 0;
        try
        {
            using var inflate = new ZLibStream(compressed, CompressionMode.Decompress);
            inflate.ReadExactly(raw);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("the PNG picture is cut short", e);
        }

        var pixels = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var source = raw.AsSpan((y * (stride + 1)) + 1, stride);
            var row = pixels.AsSpan(y * stride, stride);
            var previous = y == 0 ? ReadOnlySpan<byte>.Empty : pixels.AsSpan((y - 1) * stride, stride);
            Unfilter(filter, source, row, previous, channels);
        }

        if (channels == 4)
        {
            return (width, height, pixels);
        }

        var rgba = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            rgba[i * 4] = pixels[i * 3];
            rgba[(i * 4) + 1] = pixels[(i * 3) + 1];
            rgba[(i * 4) + 2] = pixels[(i * 3) + 2];
            rgba[(i * 4) + 3] = 255;
        }

        return (width, height, rgba);
    }

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        var candidate = new byte[stride];
        var best = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var row = rgba.Slice(y * stride, stride);
            var previous = y == 0 ? ReadOnlySpan<byte>.Empty : rgba.Slice((y - 1) * stride, stride);
            byte bestFilter = 0;
            var bestCost = long.MaxValue;
            for (byte filter = 0; filter <= 4; filter++)
            {
                Filter(filter, row, previous, candidate);
                long cost = 0;
                foreach (var b in candidate)
                {
                    cost += b < 128 ? b : 256 - b;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestFilter = filter;
                    candidate.CopyTo(best, 0);
                }
            }

            raw[y * (stride + 1)] = bestFilter;
            best.CopyTo(raw, (y * (stride + 1)) + 1);
        }

        using var data = new MemoryStream();
        using (var deflate = new ZLibStream(data, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            deflate.Write(raw);
        }

        using var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(output, "IHDR"u8, header);
        WriteChunk(output, "IDAT"u8, data.ToArray());
        WriteChunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    private static void Unfilter(byte filter, ReadOnlySpan<byte> source, Span<byte> row, ReadOnlySpan<byte> previous, int bytesPerPixel)
    {
        for (var i = 0; i < source.Length; i++)
        {
            var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
            var up = previous.IsEmpty ? 0 : previous[i];
            var upLeft = previous.IsEmpty || i < bytesPerPixel ? 0 : previous[i - bytesPerPixel];
            row[i] = filter switch
            {
                0 => source[i],
                1 => (byte)(source[i] + left),
                2 => (byte)(source[i] + up),
                3 => (byte)(source[i] + ((left + up) / 2)),
                4 => (byte)(source[i] + Paeth(left, up, upLeft)),
                _ => throw new InvalidDataException("unknown PNG row filter " + filter),
            };
        }
    }

    private static void Filter(byte filter, ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Span<byte> output)
    {
        const int bytesPerPixel = 4;
        for (var i = 0; i < row.Length; i++)
        {
            var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
            var up = previous.IsEmpty ? 0 : previous[i];
            var upLeft = previous.IsEmpty || i < bytesPerPixel ? 0 : previous[i - bytesPerPixel];
            output[i] = filter switch
            {
                0 => row[i],
                1 => (byte)(row[i] - left),
                2 => (byte)(row[i] - up),
                3 => (byte)(row[i] - ((left + up) / 2)),
                _ => (byte)(row[i] - Paeth(left, up, upLeft)),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> body)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(number, (uint)body.Length);
        output.Write(number);
        output.Write(type);
        output.Write(body);
        var crc = 0xFFFFFFFFu;
        foreach (var b in type)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        foreach (var b in body)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        BinaryPrimitives.WriteUInt32BigEndian(number, crc ^ 0xFFFFFFFFu);
        output.Write(number);
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
