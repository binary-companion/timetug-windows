using TimeTug.Platform.Broker;
using TimeTug.Presentation.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TimeTug.Platform.Auth;

/// <summary>The three window calls the foreground request makes, so a test can count them.</summary>
internal interface IForegroundWindowApi
{
    /// <summary>False once the window has been destroyed (a window can close between being registered and being asked for).</summary>
    bool IsWindow(nint window);

    bool IsIconic(nint window);

    void Restore(nint window);

    /// <summary>Asks Windows to make the window the foreground window. Windows may refuse (its foreground lock), which flashes the taskbar button.</summary>
    bool SetForeground(nint window);
}

/// <summary>
/// After a browser sign-in redirects back, brings the window the person last used forward: restores it if it is minimized, then makes the ordinary activate
/// request. Nothing else: no simulated input and no thread-input attachment. Windows may refuse, and then the landing page and the waiting row already say
/// what to do. With no TimeTug window open (the hidden message window does not count) it does nothing.
/// </summary>
public sealed class SignInForegroundRequester : ISignInReturnNotifier
{
    private readonly BrokerParentWindow _windows;
    private readonly IForegroundWindowApi _api;
    private readonly IDiagnostics? _diagnostics;

    public SignInForegroundRequester(BrokerParentWindow windows, IDiagnostics? diagnostics = null)
        : this(windows, new Win32ForegroundWindowApi(), diagnostics)
    {
    }

    internal SignInForegroundRequester(BrokerParentWindow windows, IForegroundWindowApi api, IDiagnostics? diagnostics = null)
    {
        _windows = windows;
        _api = api;
        _diagnostics = diagnostics;
    }

    public void SignInReturned()
    {
        try
        {
            if (_windows.OpenWindowHandle is not { } window)
            {
                return;
            }

            if (!_api.IsWindow(window))
            {
                return;
            }

            if (_api.IsIconic(window))
            {
                _api.Restore(window);
            }

            if (!_api.SetForeground(window))
            {
                _diagnostics?.Record("info", "signin", "foreground: Windows kept the foreground");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _diagnostics?.Record("warning", "signin", $"foreground: failed ({ex.GetType().Name})");
        }
    }

    private sealed class Win32ForegroundWindowApi : IForegroundWindowApi
    {
        public bool IsWindow(nint window) => PInvoke.IsWindow(new HWND(window));

        public bool IsIconic(nint window) => PInvoke.IsIconic(new HWND(window));

        public void Restore(nint window) => PInvoke.ShowWindow(new HWND(window), SHOW_WINDOW_CMD.SW_RESTORE);

        public bool SetForeground(nint window) => PInvoke.SetForegroundWindow(new HWND(window));
    }
}
