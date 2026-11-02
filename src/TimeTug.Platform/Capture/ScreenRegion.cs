#if SELFTEST
using System.Runtime.InteropServices;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Capture;

/// <summary>What a region of the screen looked like: where the picture was saved and how bright it is on average (0 black, 255 white).</summary>
public sealed record RegionCapture(string? Path, double MeanLuminance, int DistinctColours, string? Error)
{
    public bool Succeeded => Path is not null && DistinctColours > 1;
}

/// <summary>
/// Self-test only (it does not exist in a Release build): copies a rectangle of the desktop as the person sees it, windows of other kinds
/// included (a native menu is a window of its own, which a capture of the app's window never contains), and finds the native menu that is open.
/// </summary>
public static partial class ScreenRegion
{
    private const uint SrcCopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;

    /// <summary>The window class every Win32 popup menu has.</summary>
    private const string MenuClass = "#32768";

    /// <summary>The bounds of the largest popup menu that is visible now (the tray icon's menu is one), or null when none is.</summary>
    public static PixelRect? OpenNativeMenu()
    {
        PixelRect? best = null;
        var name = new char[16];
        _ = EnumWindows(
            (hwnd, _) =>
            {
                var length = GetClassName(hwnd, name, name.Length);
                if (length == MenuClass.Length && new string(name, 0, length) == MenuClass && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var r))
                {
                    var found = new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
                    if (best is not { } current || found.Width * found.Height > current.Width * current.Height)
                    {
                        best = found;
                    }
                }

                return 1;
            },
            0);
        return best;
    }

    /// <summary>Every popup menu window there is, visible or not, as text (for a note when a menu did not show where it should).</summary>
    public static string DescribeMenus()
    {
        var lines = new List<string>();
        var name = new char[16];
        _ = EnumWindows(
            (hwnd, _) =>
            {
                var length = GetClassName(hwnd, name, name.Length);
                if (length == MenuClass.Length && new string(name, 0, length) == MenuClass && GetWindowRect(hwnd, out var r))
                {
                    lines.Add($"{(IsWindowVisible(hwnd) ? "visible" : "hidden")} {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}");
                }

                return 1;
            },
            0);
        return lines.Count == 0 ? "no menu windows" : string.Join("; ", lines);
    }

    /// <summary>Copies the rectangle of the screen to <paramref name="path"/> and measures its mean brightness.</summary>
    public static async Task<RegionCapture> CaptureAsync(PixelRect area, string path)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return new RegionCapture(null, 0, 0, $"region is {area.Width}x{area.Height}");
        }

        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, area.Width, area.Height);
        var previous = SelectObject(memory, bitmap);
        try
        {
            if (!BitBlt(memory, 0, 0, area.Width, area.Height, screen, area.X, area.Y, SrcCopy | CaptureBlt))
            {
                return new RegionCapture(null, 0, 0, "BitBlt failed");
            }

            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = area.Width, Height = -area.Height, Planes = 1, BitCount = 32 };
            var pixels = new byte[area.Width * area.Height * 4];
            if (GetDIBits(memory, bitmap, 0, (uint)area.Height, pixels, ref info, 0) == 0)
            {
                return new RegionCapture(null, 0, 0, "GetDIBits returned no lines");
            }

            double sum = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                pixels[i + 3] = 255;
                sum += (0.0722 * pixels[i]) + (0.7152 * pixels[i + 1]) + (0.2126 * pixels[i + 2]);
            }

            await PngWriter.WriteAsync(pixels, area.Width, area.Height, path);
            return new RegionCapture(path, sum / (pixels.Length / 4), PixelAnalysis.CountDistinctColours(pixels, 1000), null);
        }
        finally
        {
            _ = SelectObject(memory, previous);
            _ = DeleteObject(bitmap);
            _ = DeleteDC(memory);
            _ = ReleaseDC(0, screen);
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

    private delegate int EnumWindowsProc(nint hwnd, nint parameter);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetClassName(nint hwnd, [Out] char[] text, int max);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hwnd, out Rect rect);

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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(nint hdcDest, int x, int y, int w, int h, nint hdcSrc, int srcX, int srcY, uint rop);

    [LibraryImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetDIBits(nint hdc, nint bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfo info, uint usage);
}
#endif
