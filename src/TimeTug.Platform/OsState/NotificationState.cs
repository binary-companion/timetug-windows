using Windows.Win32;
using Windows.Win32.UI.Shell;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.OsState;

/// <summary>What <c>SHQueryUserNotificationState</c> reports (the values of <c>QUERY_USER_NOTIFICATION_STATE</c>).</summary>
public enum UserNotificationState
{
    Unknown = 0,
    NotPresent = 1,
    Busy = 2,
    RunningD3DFullScreen = 3,
    PresentationMode = 4,
    AcceptsNotifications = 5,
    QuietTime = 6,
    App = 7,
}

/// <summary>
/// Whether Windows lets a full-screen window through and whether a toast would be accepted.
/// <para>
/// Only an exclusive full-screen (Direct3D) app and presentation mode stop a topmost window; a "busy" full-screen
/// window such as a video player or borderless game does not, so it still gets the full tug.
/// <see cref="ToastsAllowed"/> is false when Windows reports quiet time (Do Not Disturb or Focus) or notifications are
/// switched off for the app. Windows reports "do not display" for exclusive full-screen and presentation mode too, yet
/// still accepts and queues a toast there, so those states do not count against it: the toast is the fallback for them.
/// </para>
/// <para>
/// Windows has no notification for "full-screen ended", so while the controller asks for it the state is polled
/// with a <see cref="PeriodicTimer"/> (every 5 seconds by default) and <see cref="FullScreenEnded"/> is raised on the
/// transition.
/// </para>
/// </summary>
public sealed class NotificationState : INotificationState, IDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(5);

    private readonly TimeProvider _time;
    private readonly Func<UserNotificationState> _query;
    private readonly Func<bool> _toastsEnabled;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _watch;
    private bool _disposed;

    /// <param name="time">Drives the polling timer.</param>
    /// <param name="query">Reads the state from Windows; defaults to <c>SHQueryUserNotificationState</c>.</param>
    /// <param name="toastsEnabled">Whether notifications are enabled for the app (the toast service knows); defaults to true.</param>
    public NotificationState(TimeProvider time, Func<UserNotificationState>? query = null, Func<bool>? toastsEnabled = null, TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _query = query ?? QueryWindows;
        _toastsEnabled = toastsEnabled ?? (() => true);
        _interval = pollInterval ?? DefaultPollInterval;
    }

    public bool CanShowFullScreen => Classify(_query()).CanShowFullScreen;

    public bool ToastsAllowed => Classify(_query()).ToastsAllowed && SafeToastsEnabled();

    public event Action? FullScreenEnded;

    /// <summary>Whether the polling timer currently runs (tests and diagnostics).</summary>
    public bool IsWatching
    {
        get
        {
            lock (_gate)
            {
                return _watch is not null;
            }
        }
    }

    /// <summary>Maps the Windows state to the two answers the tug needs.</summary>
    public static (bool CanShowFullScreen, bool ToastsAllowed) Classify(UserNotificationState state) => state switch
    {
        UserNotificationState.RunningD3DFullScreen or UserNotificationState.PresentationMode => (false, true),
        UserNotificationState.QuietTime => (true, false),
        _ => (true, true),
    };

    public void SetWatchingForFullScreenEnd(bool watching)
    {
        lock (_gate)
        {
            if (_disposed || watching == (_watch is not null))
            {
                return;
            }

            if (!watching)
            {
                _watch!.Cancel();
                _watch.Dispose();
                _watch = null;
                return;
            }

            _watch = new CancellationTokenSource();
            _ = PollAsync(CanShowFullScreen, _watch.Token);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watch?.Cancel();
            _watch?.Dispose();
            _watch = null;
        }
    }

    private async Task PollAsync(bool couldShow, CancellationToken token)
    {
        using var timer = new PeriodicTimer(_interval, _time);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                var canShow = CanShowFullScreen;
                if (!couldShow && canShow)
                {
                    FullScreenEnded?.Invoke();
                }

                couldShow = canShow;
            }
        }
        catch (OperationCanceledException)
        {
            // Watching stopped.
        }
    }

    private bool SafeToastsEnabled()
    {
        try
        {
            return _toastsEnabled();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return true;
        }
    }

    private static unsafe UserNotificationState QueryWindows()
    {
        QUERY_USER_NOTIFICATION_STATE state;
        return PInvoke.SHQueryUserNotificationState(&state).Succeeded ? (UserNotificationState)(int)state : UserNotificationState.Unknown;
    }
}
