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
    private const uint Move = 0x0001;
    private const uint Absolute = 0x8000;
    private const uint VirtualDesk = 0x4000;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;

    /// <summary>Moves the pointer to the physical-pixel position (no click). Used to bring up an auto-hidden taskbar. False when Windows refused.</summary>
    public static bool MoveTo(int x, int y) => SetCursorPos(x, y);

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

    /// <summary>Moves the pointer to the physical-pixel position and clicks the right button. False when Windows refused either step.</summary>
    public static bool RightClickAt(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            return false;
        }

        ReadOnlySpan<Input> clicks = [Button(RightDown), Button(RightUp)];
        return SendInput((uint)clicks.Length, clicks, Marshal.SizeOf<Input>()) == clicks.Length;
    }

    /// <summary>Two left clicks in a row at the position, as fast as a double click. False when Windows refused a step.</summary>
    public static bool DoubleClickAt(int x, int y)
    {
        if (!SetCursorPos(x, y))
        {
            return false;
        }

        ReadOnlySpan<Input> clicks = [Button(LeftDown), Button(LeftUp), Button(LeftDown), Button(LeftUp)];
        return SendInput((uint)clicks.Length, clicks, Marshal.SizeOf<Input>()) == clicks.Length;
    }

    /// <summary>
    /// Presses the left button at the start position, moves to the end position in small steps with the button held (so the window under it
    /// sees a drag, not a jump) and releases it there. False when Windows refused a step.
    /// </summary>
    public static bool DragFromTo(int fromX, int fromY, int toX, int toY, int steps = 12)
    {
        if (!SetCursorPos(fromX, fromY))
        {
            return false;
        }

        ReadOnlySpan<Input> down = [Button(LeftDown)];
        if (SendInput(1, down, Marshal.SizeOf<Input>()) != 1)
        {
            return false;
        }

        var ok = true;
        for (var i = 1; i <= steps; i++)
        {
            ok &= MoveWithInput(fromX + ((toX - fromX) * i / steps), fromY + ((toY - fromY) * i / steps));
            Thread.Sleep(15);
        }

        ReadOnlySpan<Input> up = [Button(LeftUp)];
        return SendInput(1, up, Marshal.SizeOf<Input>()) == 1 && ok;
    }

    /// <summary>Moves the pointer the way a mouse does (an input event, so a window that tracks the pointer sees it move), not by setting its position.</summary>
    private static bool MoveWithInput(int x, int y)
    {
        // The virtual screen in physical pixels, from the monitors themselves: GetSystemMetrics answers in the system's scale, which is not the monitors'
        // when they differ.
        var monitors = OsState.MonitorService.Enumerate();
        if (monitors.Count == 0)
        {
            return SetCursorPos(x, y);
        }

        var left = monitors.Min(m => m.X);
        var top = monitors.Min(m => m.Y);
        var width = Math.Max(2, monitors.Max(m => m.X + m.Width) - left);
        var height = Math.Max(2, monitors.Max(m => m.Y + m.Height) - top);
        var move = new Input
        {
            Type = InputMouse,
            Mouse = new MouseData
            {
                Dx = (int)((x - left) * 65535L / (width - 1)),
                Dy = (int)((y - top) * 65535L / (height - 1)),
                Flags = Move | Absolute | VirtualDesk,
            },
        };
        ReadOnlySpan<Input> inputs = [move];
        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
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
