using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Hotkey;

/// <summary>
/// Holds at most one global shortcut with Windows (<c>RegisterHotKey</c> and <c>UnregisterHotKey</c> on the app's hidden window) and
/// reports its presses (<c>WM_HOTKEY</c>). A seam so <see cref="HotkeyService"/> can be tested without a desktop.
/// </summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>
    /// Registers the shortcut. False when Windows refuses it (another program holds it). A registrar holds one shortcut: it must be
    /// unregistered first, and asking again while one is held is a programming error (<see cref="InvalidOperationException"/>).
    /// </summary>
    Task<bool> RegisterAsync(HotkeyBinding binding);

    /// <summary>Releases the shortcut; nothing happens when none is held.</summary>
    Task UnregisterAsync();

    /// <summary>The registered shortcut was pressed. Raised on the window's own thread; handlers must return quickly.</summary>
    event Action? Pressed;
}
