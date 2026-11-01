using TimeTug.Platform.Broker;

namespace TimeTug.App;

/// <summary>
/// The window the Windows sign-in dialog is parented to. The broker requires a handle, and the dialog appears in front when its parent is a visible window of
/// this process (the spike's S7). A window that can start a sign-in registers itself while it is open; the most recently opened one is used. With none
/// open the fallback is the hidden top-level message window, which the spike found not dependable (no dialog was seen when every window was hidden), so
/// reconnecting is offered only from windows (Settings > Accounts, the welcome window), never from the flyout.
/// </summary>
internal sealed class BrokerParentWindow : IBrokerParentWindow
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

    /// <summary>A window that is open and can show the sign-in dialog in front of it.</summary>
    public void Register(nint handle)
    {
        lock (_gate)
        {
            _open.Remove(handle);
            _open.Add(handle);
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
