using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TimeTug.Platform.OsState;

/// <summary>
/// A hidden window on its own thread, created through CsWin32, that receives the notifications the OS-state
/// services need. WinUI windows come and go, and <c>WTSRegisterSessionNotification</c> needs an <c>HWND</c>.
/// <para>
/// It is a hidden top-level window rather than a message-only window: message-only windows never receive broadcast
/// messages, and <c>WM_DISPLAYCHANGE</c>, <c>WM_TIMECHANGE</c>, <c>WM_SETTINGCHANGE</c> and
/// <c>WM_POWERBROADCAST</c> are broadcasts. It is never shown and never activated.
/// </para>
/// </summary>
public sealed unsafe class MessageWindow : IWindowMessageSource, IDisposable
{
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly string _className = "TimeTug.MessageWindow." + Guid.NewGuid().ToString("N");
    // Each window runs its own thread and the system calls the window procedure on that thread, so the owner is found here.
    [ThreadStatic]
    private static MessageWindow? t_owner;

    // A private message that makes the window's thread run the queued calls (WM_APP + 1).
    private const uint RunCallsMessage = 0x8001;

    private readonly System.Collections.Concurrent.ConcurrentQueue<PendingCall> _calls = new();
    private HWND _window;
    private Exception? _startupError;
    private int _disposed;

    public MessageWindow()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "TimeTug message window" };
        _thread.Start();
        _ready.Wait();
        if (_startupError is { } error)
        {
            throw new InvalidOperationException("The message window could not be created.", error);
        }
    }

    nint IWindowMessageSource.Handle => _window;

    public event Action<WindowMessage>? MessageReceived;

    /// <summary>
    /// Runs <paramref name="work"/> on the window's own thread and returns its result. Some calls belong to the thread that owns the
    /// window (<c>RegisterHotKey</c> with this window fails from any other thread). Faults with <see cref="ObjectDisposedException"/>
    /// when the window is gone.
    /// </summary>
    public Task<T> InvokeAsync<T>(Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (Volatile.Read(ref _disposed) == 1 || _window == default)
        {
            completion.SetException(new ObjectDisposedException(nameof(MessageWindow)));
            return completion.Task;
        }

        _calls.Enqueue(new PendingCall(
            () =>
            {
                try
                {
                    completion.TrySetResult(work());
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    completion.TrySetException(e);
                }
            },
            () => completion.TrySetException(new ObjectDisposedException(nameof(MessageWindow)))));
        if (!PInvoke.PostMessage(_window, RunCallsMessage, 0, 0))
        {
            RunCalls(cancel: true);
        }

        return completion.Task;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_window != default)
        {
            PInvoke.PostMessage(_window, PInvoke.WM_CLOSE, 0, 0);
        }

        _thread.Join(TimeSpan.FromSeconds(5));
        RunCalls(cancel: true);
        _ready.Dispose();
    }

    private void Run()
    {
        var instance = new HINSTANCE(Marshal.GetHINSTANCE(typeof(MessageWindow).Module));
        t_owner = this;
        try
        {
            fixed (char* className = _className)
            {
                var windowClass = new WNDCLASSEXW
                {
                    cbSize = (uint)sizeof(WNDCLASSEXW),
                    lpfnWndProc = &WindowProcedure,
                    hInstance = instance,
                    lpszClassName = className,
                };
                if (PInvoke.RegisterClassEx(&windowClass) == 0)
                {
                    throw new InvalidOperationException("RegisterClassEx failed (" + Marshal.GetLastWin32Error() + ").");
                }

                _window = PInvoke.CreateWindowEx(
                    WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_NOACTIVATE,
                    className,
                    className,
                    WINDOW_STYLE.WS_POPUP,
                    0,
                    0,
                    0,
                    0,
                    default,
                    default,
                    instance,
                    null);
            }

            if (_window == default)
            {
                throw new InvalidOperationException("CreateWindowEx failed (" + Marshal.GetLastWin32Error() + ").");
            }
        }
        catch (InvalidOperationException e)
        {
            _startupError = e;
            _ready.Set();
            return;
        }

        _ready.Set();
        MSG message;
        while (PInvoke.GetMessage(&message, default, 0, 0))
        {
            PInvoke.TranslateMessage(&message);
            PInvoke.DispatchMessage(&message);
        }

        fixed (char* className = _className)
        {
            PInvoke.UnregisterClass(className, instance);
        }

        t_owner = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        var owner = t_owner;
        switch (message)
        {
            case PInvoke.WM_CLOSE:
                PInvoke.DestroyWindow(window);
                return new LRESULT(0);
            case PInvoke.WM_DESTROY:
                PInvoke.PostQuitMessage(0);
                return new LRESULT(0);
            case RunCallsMessage:
                owner?.RunCalls(cancel: false);
                return new LRESULT(0);
            case WindowMessage.DisplayChange or WindowMessage.TimeChange or WindowMessage.SettingChange
                or WindowMessage.PowerBroadcast or WindowMessage.SessionChange or WindowMessage.Hotkey:
                owner?.Raise(message, wParam, lParam);
                return new LRESULT(0);
            default:
                return PInvoke.DefWindowProc(window, message, wParam, lParam);
        }
    }

    private void RunCalls(bool cancel)
    {
        while (_calls.TryDequeue(out var call))
        {
            if (cancel)
            {
                call.Cancel();
            }
            else
            {
                call.Run();
            }
        }
    }

    private sealed record PendingCall(Action Run, Action Cancel);

    private void Raise(uint message, WPARAM wParam, LPARAM lParam)
    {
        // The string behind WM_SETTINGCHANGE's lParam is valid only during this call.
        var text = message == WindowMessage.SettingChange && lParam.Value != 0 ? Marshal.PtrToStringUni(lParam.Value) : null;
        try
        {
            MessageReceived?.Invoke(new WindowMessage(message, wParam.Value, lParam.Value, text));
        }
#pragma warning disable CA1031 // A handler fault must never take down the message loop.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }
}
