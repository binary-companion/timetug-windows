using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TimeTug.Platform.Instance;

/// <summary>A notice shown before anything else has started (no window, no engine, no tray): the person reads it and the process exits.</summary>
public interface IStartupNotice
{
    /// <summary>Shows <paramref name="message"/> and returns when the person has dismissed it.</summary>
    void Show(string title, string message);
}

/// <summary>A plain system message box. It needs nothing from WinUI, so it works at the very start of the process, before any of the app exists.</summary>
public sealed class MessageBoxStartupNotice : IStartupNotice
{
    public void Show(string title, string message)
    {
        PInvoke.MessageBox(default, message, title, MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONINFORMATION | MESSAGEBOX_STYLE.MB_SETFOREGROUND);
    }
}
