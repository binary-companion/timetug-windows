#if SELFTEST
using System.Runtime.InteropServices;

namespace TimeTug.Platform.Capture;

/// <summary>
/// Self-test only (it does not exist in a Release build): presses a key chord the way a keyboard would, through <c>SendInput</c>. The app
/// itself never injects input. A scenario uses it to learn whether a real key press reaches a control, which depends on that window really
/// having the keyboard (a runner without a foreground window, or a helper that took the focus, will not), so the answer is a note and not a check.
/// </summary>
public static partial class KeyboardInput
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;

    /// <summary>Presses the modifiers (in order), taps the key, then releases the modifiers. False when Windows refused any step.</summary>
    public static bool Chord(ReadOnlySpan<ushort> modifiers, ushort key)
    {
        var steps = new List<Input>();
        foreach (var modifier in modifiers)
        {
            steps.Add(Key(modifier, 0));
        }

        steps.Add(Key(key, 0));
        steps.Add(Key(key, KeyUp));
        for (var i = modifiers.Length - 1; i >= 0; i--)
        {
            steps.Add(Key(modifiers[i], KeyUp));
        }

        var inputs = steps.ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) == inputs.Length;
    }

    private static Input Key(ushort virtualKey, uint flags) => new() { Type = InputKeyboard, Keyboard = new KeyData { VirtualKey = virtualKey, Flags = flags } };

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    // INPUT is 40 bytes on 64-bit Windows: the type, then a union as large as MOUSEINPUT. The padding makes the keyboard member the same size.
    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public KeyData Keyboard;
        public long Padding;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial uint SendInput(uint count, Input[] inputs, int size);
}
#endif
