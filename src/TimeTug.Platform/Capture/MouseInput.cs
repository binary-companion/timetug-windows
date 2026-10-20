#if SELFTEST
using System.Runtime.InteropServices;

namespace TimeTug.Platform.Capture;

/// <summary>
/// Self-test only (it does not exist in a Release build): presses the left mouse button at a screen position, as a person's click
/// would arrive. The app itself never injects input; a scenario uses this to prove that a first click on the tug takes the keyboard.
/// </summary>
public static partial class MouseInput
{
    private const uint InputMouse = 0;
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;

    /// <summary>Moves the pointer to the physical-pixel position and clicks. False when Windows refused either step.</summary>
    public static bool ClickAt(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            return false;
        }

        ReadOnlySpan<Input> clicks = [Button(LeftDown), Button(LeftUp)];
        return SendInput((uint)clicks.Length, clicks, Marshal.SizeOf<Input>()) == clicks.Length;
    }

    private static Input Button(uint flags) => new() { Type = InputMouse, Mouse = new MouseData { Flags = flags } };

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public int Dx;
        public int Dy;
        public uint Data;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public MouseData Mouse;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SendInput(uint count, ReadOnlySpan<Input> inputs, int size);
}
#endif
