namespace TimeTug.Platform.Broker;

/// <summary>
/// The window the Windows sign-in dialog is parented to. The broker requires a handle, and the dialog appears in front when its parent is a visible window of
/// this process (the spike's S7). A window that can start a sign-in registers itself while it is open and is touched whenever it is activated; the most recently active one is used. With none
/// open the fallback is the hidden top-level message window, which the spike found not dependable (no dialog was seen when every window was hidden), so
/// reconnecting is offered only from windows (Settings > Accounts, the welcome window), never from the flyout.
/// </summary>
public sealed class BrokerParentWindow : IBrokerParentWindow
{
    private readonly Func<nint> _fallback;
    private readonly List<nint> _open = [];
    private readonly object _gate = new();

    public BrokerParentWindow(Func<nint> fallback) => _fallback = fallback;

    public nint Handle
    {
        get
        {
            lock (_gate)
            {
                if (_open.Count > 0)
                {
                    return _open[^1];
                }
            }

            return _fallback();
        }
    }

    /// <summary>
    /// The most recently active registered window, or null when none is open. Unlike <see cref="Handle"/> it never falls back to the hidden message window: a
    /// browser sign-in that comes back brings a window forward only when there is a window to bring.
    /// </summary>
    public nint? OpenWindowHandle
    {
        get
        {
            lock (_gate)
            {
                return _open.Count > 0 ? _open[^1] : null;
            }
        }
    }

    /// <summary>A window that is open and can show the sign-in dialog in front of it.</summary>
    public void Register(nint handle)
    {
        lock (_gate)
        {
            _open.Remove(handle);
            _open.Add(handle);
        }
    }

    /// <summary>A registered window became the active one. An unregistered handle is ignored.</summary>
    public void Touch(nint handle)
    {
        lock (_gate)
        {
            if (_open.Remove(handle))
            {
                _open.Add(handle);
            }
        }
    }

    /// <summary>The window closed.</summary>
    public void Unregister(nint handle)
    {
        lock (_gate)
        {
            _open.Remove(handle);
        }
    }
}
