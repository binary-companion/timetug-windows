using System.Buffers.Binary;

namespace TimeTug.Platform.Capture;

/// <summary>Pure checks on captured pixels (32 bits per pixel), used to reject blank screenshots.</summary>
public static class PixelAnalysis
{
    /// <summary>Counts distinct pixel values, stopping once <paramref name="limit"/> is reached.</summary>
    public static int CountDistinctColours(ReadOnlySpan<byte> pixels32, int limit = int.MaxValue)
    {
        var seen = new HashSet<uint>();
        for (var offset = 0; offset + 4 <= pixels32.Length; offset += 4)
        {
            seen.Add(BinaryPrimitives.ReadUInt32LittleEndian(pixels32.Slice(offset, 4)));
            if (seen.Count >= limit)
            {
                break;
            }
        }

        return seen.Count;
    }

    /// <summary>True when the image has at most one distinct colour (including an empty image).</summary>
    public static bool IsBlank(ReadOnlySpan<byte> pixels32) => CountDistinctColours(pixels32, limit: 2) <= 1;
}
