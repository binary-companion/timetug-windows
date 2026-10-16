using System.Runtime.InteropServices;

namespace TimeTug.Platform.Capture;

/// <summary>The outcome of one capture attempt.</summary>
/// <param name="Method">Which technique was used.</param>
/// <param name="Path">The PNG written, or null when the attempt failed.</param>
/// <param name="DistinctColours">Distinct colours found, capped at 1000.</param>
/// <param name="Error">Why the attempt failed, or null.</param>
public sealed record CaptureOutcome(string Method, string? Path, int DistinctColours, string? Error)
{
    public bool Succeeded => Path is not null && DistinctColours > 1;
}

/// <summary>Captures a top-level window to a PNG without needing the window to be visible on a physical display.</summary>
public static partial class ScreenCapture
{
    private const uint PwRenderFullContent = 0x2;

    /// <summary>Captures the window with PrintWindow (PW_RENDERFULLCONTENT), which asks the window to paint itself.</summary>
    public static async Task<CaptureOutcome> WindowAsync(nint hwnd, string path)
    {
        const string method = "PrintWindow";
        try
        {
            // Capture the whole window (frame included) so the title bar is part of the picture.
            if (!GetWindowRect(hwnd, out var outer))
            {
                return new CaptureOutcome(method, null, 0, "GetWindowRect failed");
            }

            var width = outer.Right - outer.Left;
            var height = outer.Bottom - outer.Top;
            if (width <= 0 || height <= 0)
            {
                return new CaptureOutcome(method, null, 0, $"window is {width}x{height}");
            }

            var screen = GetDC(0);
            var memory = CreateCompatibleDC(screen);
            var bitmap = CreateCompatibleBitmap(screen, width, height);
            var previous = SelectObject(memory, bitmap);
            try
            {
                if (!PrintWindow(hwnd, memory, PwRenderFullContent))
                {
                    return new CaptureOutcome(method, null, 0, "PrintWindow returned false");
                }

                var info = new BitmapInfo
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfo>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                };
                var pixels = new byte[width * height * 4];
                var lines = GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref info, 0);
                if (lines == 0)
                {
                    return new CaptureOutcome(method, null, 0, "GetDIBits returned no lines");
                }

                // GDI leaves the alpha byte at 0; make the picture opaque.
                for (var i = 3; i < pixels.Length; i += 4)
                {
                    pixels[i] = 255;
                }

                await PngWriter.WriteAsync(pixels, width, height, path);
                return new CaptureOutcome(method, path, PixelAnalysis.CountDistinctColours(pixels, 1000), null);
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(bitmap);
                DeleteDC(memory);
                ReleaseDC(0, screen);
            }
        }
        catch (Exception ex) when (ex is COMException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return new CaptureOutcome(method, null, 0, ex.GetType().Name + ": " + ex.Message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColourUsed;
        public uint ColourImportant;
        public uint Colour0;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(nint hwnd, nint hdc, uint flags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint GetDC(nint hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int ReleaseDC(nint hwnd, nint hdc);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint CreateCompatibleBitmap(nint hdc, int width, int height);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial nint SelectObject(nint hdc, nint obj);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(nint obj);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfo info, uint usage);
}
