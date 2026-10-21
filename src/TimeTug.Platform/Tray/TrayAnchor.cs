using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tray;

/// <summary>The three questions the anchor asks the taskbar. A seam so the order and the checks are testable without a taskbar.</summary>
public interface ITaskbarProbe
{
    /// <summary>The icon's own rectangle (<c>Shell_NotifyIconGetRect</c>); null when Windows will not say.</summary>
    PixelRect? IconRect(Guid iconId);

    /// <summary>The notification-area overflow button (the chevron), found through UI Automation on the taskbar; null when there is none.</summary>
    PixelRect? OverflowButtonRect();

    /// <summary>The taskbar's notification area; null when there is no taskbar.</summary>
    PixelRect? NotificationAreaRect();

    /// <summary>The taskbar's UI Automation elements, one per line, for a person to read when the anchor is wrong (the diagnostics probe).</summary>
    string Describe();
}

/// <summary>
/// Finds the tray icon on screen. In order: the icon's own rectangle; when the icon lives in the overflow flyout (so Windows has no
/// place for it in the notification area), the overflow button; and when even that is not found, the notification area itself, whose
/// corner nearest the clock is where the flyout then goes. A rectangle that is empty or on no monitor does not count as an answer.
/// <para>
/// The overflow button is found through UI Automation, which waits on Explorer (up to two seconds when Explorer is slow), so the
/// UI thread never asks the whole chain: <see cref="Peek"/> answers from the last good result, or from the two quick questions (the
/// icon's own rectangle, the notification area); <see cref="Refresh"/> runs the whole chain on a pool thread, keeps the result and
/// raises <see cref="Changed"/> when it differs from what <see cref="Peek"/> last said. The kept result is forgotten when the displays
/// or the taskbar settings change (<see cref="Invalidate"/>) and when a look finds nothing.
/// </para>
/// </summary>
public sealed class TrayAnchor : ITrayAnchor, IDisposable
{
    private const string Category = "tray";

    private readonly Func<Guid?> _iconId;
    private readonly ITaskbarProbe _probe;
    private readonly IMonitorService? _monitors;
    private readonly IDiagnostics? _diagnostics;
    private readonly object _gate = new();
    private readonly Action? _onMonitorsChanged;
    private AnchorRect? _cache;
    private AnchorRect? _served;
    private int _generation;
    private bool _refreshing;
    private bool _disposed;
    private AnchorSource? _lastSource;
    private bool _reported;

    /// <param name="iconId">The icon's identifier, or null before the icon exists.</param>
    /// <param name="probe">What reads the taskbar; <see cref="WindowsTaskbarProbe"/> in the app.</param>
    /// <param name="monitors">When given, a rectangle that is on none of them is rejected (a stale answer after a display change).</param>
    public TrayAnchor(Func<Guid?> iconId, ITaskbarProbe probe, IMonitorService? monitors = null, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(iconId);
        ArgumentNullException.ThrowIfNull(probe);
        _iconId = iconId;
        _probe = probe;
        _monitors = monitors;
        _diagnostics = diagnostics;
        if (monitors is not null)
        {
            // A display change (including a scaling change) can move the icon: the kept answer may be on a screen that is gone.
            _onMonitorsChanged = Invalidate;
            monitors.Changed += _onMonitorsChanged;
        }
    }

    public event Action? Changed;

    public AnchorRect? Peek()
    {
        lock (_gate)
        {
            if (_cache is { } kept)
            {
                _served = kept;
                return kept;
            }
        }

        // The two questions that do not wait for Explorer's UI Automation tree.
        var quick = Ask(AnchorSource.Icon, () => _iconId() is { } id ? _probe.IconRect(id) : null)
            ?? Ask(AnchorSource.NotificationArea, _probe.NotificationAreaRect);
        lock (_gate)
        {
            _served = quick;
        }

        return quick;
    }

    public void Refresh()
    {
        int generation;
        lock (_gate)
        {
            if (_refreshing || _disposed)
            {
                return;
            }

            _refreshing = true;
            generation = _generation;
        }

        _ = Task.Run(() => RefreshCore(generation));
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _cache = null;
            _generation++;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        if (_onMonitorsChanged is not null && _monitors is not null)
        {
            _monitors.Changed -= _onMonitorsChanged;
        }
    }

    private void RefreshCore(int generation)
    {
        var full = TryGetAnchor();
        bool changed;
        lock (_gate)
        {
            _refreshing = false;
            if (_disposed)
            {
                return;
            }

            // A look that ended after the displays changed describes the old layout: it is shown but not kept.
            if (generation == _generation)
            {
                _cache = full;
            }

            changed = !Equals(full, _served);
            if (changed)
            {
                _served = full;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <inheritdoc/>
    public AnchorRect? TryGetAnchor()
    {
        var answer = Ask(AnchorSource.Icon, () => _iconId() is { } id ? _probe.IconRect(id) : null)
            ?? Ask(AnchorSource.OverflowButton, _probe.OverflowButtonRect)
            ?? Ask(AnchorSource.NotificationArea, _probe.NotificationAreaRect);
        var source = answer?.Source;
        if (!_reported || source != _lastSource)
        {
            _reported = true;
            _lastSource = source;
            _diagnostics?.Record("info", Category, source is null ? "the flyout found no tray anchor" : $"the flyout anchor comes from {source}");
        }

        return answer;
    }

    private AnchorRect? Ask(AnchorSource source, Func<PixelRect?> question)
    {
        try
        {
            return question() is { } rect && IsUsable(rect) ? new AnchorRect(rect, source) : null;
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            // A taskbar that answers badly (explorer restarting, UI Automation unavailable) is the same as one that does not answer.
            _diagnostics?.Record("warning", Category, $"asking the taskbar for {source} failed ({e.GetType().Name})");
            return null;
        }
    }

    private bool IsUsable(PixelRect rect)
    {
        if (rect.IsEmpty)
        {
            return false;
        }

        var monitors = _monitors?.Monitors;
        return monitors is null or { Count: 0 } || monitors.Any(m => m.Bounds.Contains(rect.CenterX, rect.CenterY));
    }
}
