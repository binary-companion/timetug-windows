using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.RemoteDesktop;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.OsState;

/// <summary>
/// Whether the Windows session is locked, from <c>WTSRegisterSessionNotification</c> on the hidden message window.
/// App-local state for the held-tug rule: the engine's contract has no kind for lock and unlock.
/// </summary>
public sealed unsafe class SessionState : ISessionState, IDisposable
{
    private readonly IWindowMessageSource _source;
    private readonly IDiagnostics? _diagnostics;
    private int _locked;
    private bool _registered;

    /// <param name="source">The window to register on; its <see cref="IWindowMessageSource.Handle"/> may be 0 (nothing is registered then).</param>
    /// <param name="readIsLocked">Reads the initial state; defaults to asking Windows.</param>
    public SessionState(IWindowMessageSource source, Func<bool>? readIsLocked = null, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _diagnostics = diagnostics;
        _locked = (readIsLocked ?? QueryIsLocked)() ? 1 : 0;
        source.MessageReceived += OnMessage;
        if (source.Handle != 0)
        {
            _registered = PInvoke.WTSRegisterSessionNotification(new HWND(source.Handle), PInvoke.NOTIFY_FOR_THIS_SESSION);
            if (!_registered)
            {
                diagnostics?.Record("warning", "session", "lock and unlock notifications could not be registered; the tug will assume the session is unlocked");
            }
        }
    }

    public bool IsLocked => Volatile.Read(ref _locked) == 1;

    public event Action? Locked;

    public event Action? Unlocked;

    public void Dispose()
    {
        _source.MessageReceived -= OnMessage;
        if (_registered)
        {
            _registered = false;
            PInvoke.WTSUnRegisterSessionNotification(new HWND(_source.Handle));
        }
    }

    /// <summary>Asks Windows whether this session is locked (<c>WTSSessionInfoEx</c>); false when it cannot tell.</summary>
    public static bool QueryIsLocked()
    {
        PWSTR buffer = default;
        uint bytes = 0;
        if (!PInvoke.WTSQuerySessionInformation(
                HANDLE.Null, PInvoke.WTS_CURRENT_SESSION, WTS_INFO_CLASS.WTSSessionInfoEx, &buffer, &bytes))
        {
            return false;
        }

        try
        {
            if (bytes < (uint)sizeof(WTSINFOEXW))
            {
                return false;
            }

            var info = (WTSINFOEXW*)buffer.Value;
            return info->Level == 1 && info->Data.WTSInfoExLevel1.SessionFlags == (int)PInvoke.WTS_SESSIONSTATE_LOCK;
        }
        finally
        {
            PInvoke.WTSFreeMemory(buffer.Value);
        }
    }

    private void OnMessage(WindowMessage message)
    {
        if (message.Id != WindowMessage.SessionChange)
        {
            return;
        }

        if (message.WParam == WindowMessage.SessionLock)
        {
            if (Interlocked.Exchange(ref _locked, 1) == 0)
            {
                Locked?.Invoke();
            }
        }
        else if (message.WParam == WindowMessage.SessionUnlock && Interlocked.Exchange(ref _locked, 0) == 1)
        {
            Unlocked?.Invoke();
        }
    }
}
