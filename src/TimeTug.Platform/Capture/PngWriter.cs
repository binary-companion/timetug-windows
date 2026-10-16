using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TimeTug.Platform.Capture;

/// <summary>Writes straight or premultiplied BGRA pixels to a PNG file with the Windows imaging codec.</summary>
public static class PngWriter
{
    public static async Task WriteAsync(byte[] bgra, int width, int height, string path)
    {
        ArgumentNullException.ThrowIfNull(bgra);
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, 96, 96, bgra);
        await encoder.FlushAsync();

        stream.Seek(0);
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream);
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(path, bytes);
    }
}
