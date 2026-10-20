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
/// </summary>
public sealed class TrayAnchor : ITrayAnchor
{
    private const string Category = "tray";

    private readonly Func<Guid?> _iconId;
    private readonly ITaskbarProbe _probe;
    private readonly IMonitorService? _monitors;
    private readonly IDiagnostics? _diagnostics;
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
    }

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
