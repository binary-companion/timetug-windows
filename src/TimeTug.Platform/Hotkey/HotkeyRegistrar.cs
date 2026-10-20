using System.Runtime.InteropServices;
using TimeTug.Platform.OsState;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace TimeTug.Platform.Hotkey;

/// <summary>
/// <c>RegisterHotKey</c> and <c>UnregisterHotKey</c> on the app's hidden top-level window (<see cref="MessageWindow"/>). Windows ties a
/// shortcut to the thread that owns the window, so both calls run on that thread (<see cref="MessageWindow.InvokeAsync{T}"/>); the
/// window's <c>WM_HOTKEY</c> comes back through its message stream. One shortcut at a time.
/// </summary>
public sealed class HotkeyRegistrar : IHotkeyRegistrar
{
    /// <summary>The id of this app's one registration (any value from 0 to 0xBFFF belongs to the application).</summary>
    internal const int RegistrationId = 0x5454;

    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly MessageWindow _window;
    private readonly IDiagnostics? _diagnostics;
    private int _disposed;
    private bool _held;

    public HotkeyRegistrar(MessageWindow window, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        _window = window;
        _diagnostics = diagnostics;
        ((IWindowMessageSource)_window).MessageReceived += OnMessage;
    }

    public event Action? Pressed;

    public Task<bool> RegisterAsync(HotkeyBinding binding) => _window.InvokeAsync(() =>
    {
        if (_held)
        {
            throw new InvalidOperationException("a shortcut is already registered; release it first");
        }

        // MOD_NOREPEAT: holding the keys down is one press, not a stream of them.
        var modifiers = (HOT_KEY_MODIFIERS)(int)binding.Modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
        if (PInvoke.RegisterHotKey(new HWND(((IWindowMessageSource)_window).Handle), RegistrationId, modifiers, (uint)binding.VirtualKey))
        {
            _held = true;
            return true;
        }

        var error = Marshal.GetLastWin32Error();
        _diagnostics?.Record(error == ErrorHotkeyAlreadyRegistered ? "info" : "warning", "hotkey", $"Windows refused the shortcut (error {error})");
        return false;
    });

    public Task UnregisterAsync() => _window.InvokeAsync(() =>
    {
        UnregisterNow();
        return true;
    });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        ((IWindowMessageSource)_window).MessageReceived -= OnMessage;
        try
        {
            // A window that is already gone released the shortcut with itself.
            UnregisterAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
        }
    }

#if SELFTEST
    /// <summary>Self-test only: posts a <c>WM_HOTKEY</c> for this registration to the real window, as the system does when the keys are pressed.</summary>
    public bool SimulatePress() => PInvoke.PostMessage(new HWND(((IWindowMessageSource)_window).Handle), WindowMessage.Hotkey, RegistrationId, 0);
#endif

    private void UnregisterNow()
    {
        if (!_held)
        {
            return;
        }

        _held = false;
        if (!PInvoke.UnregisterHotKey(new HWND(((IWindowMessageSource)_window).Handle), RegistrationId))
        {
            _diagnostics?.Record("warning", "hotkey", $"releasing the shortcut failed (error {Marshal.GetLastWin32Error()})");
        }
    }

    private void OnMessage(WindowMessage message)
    {
        if (message.Id == WindowMessage.Hotkey && message.WParam == RegistrationId)
        {
            Pressed?.Invoke();
        }
    }
}
