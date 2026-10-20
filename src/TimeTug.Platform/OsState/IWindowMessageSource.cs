namespace TimeTug.Platform.OsState;

/// <summary>One Windows message the OS-state services care about. <see cref="Text"/> is the string payload of <c>WM_SETTINGCHANGE</c>.</summary>
public readonly record struct WindowMessage(uint Id, nuint WParam, nint LParam, string? Text = null)
{
    public const uint DisplayChange = 0x007E;
    public const uint TimeChange = 0x001E;
    public const uint SettingChange = 0x001A;
    public const uint PowerBroadcast = 0x0218;
    public const uint SessionChange = 0x02B1;

    /// <summary><c>WM_HOTKEY</c>: a shortcut registered on the window was pressed; <see cref="WParam"/> is the registration id.</summary>
    public const uint Hotkey = 0x0312;

    /// <summary><c>PBT_APMRESUMEAUTOMATIC</c>, the always-sent "the system resumed" notification.</summary>
    public const uint PowerResumeAutomatic = 0x0012;

    /// <summary><c>WTS_SESSION_LOCK</c> and <c>WTS_SESSION_UNLOCK</c>.</summary>
    public const uint SessionLock = 0x7;
    public const uint SessionUnlock = 0x8;
}

/// <summary>
/// The hidden window that receives the system's notifications. Messages arrive on its own thread; handlers must
/// return quickly and must not throw.
/// </summary>
public interface IWindowMessageSource
{
    /// <summary>The native window handle (0 for a source that has none, such as a test fake).</summary>
    nint Handle { get; }

    event Action<WindowMessage>? MessageReceived;
}
