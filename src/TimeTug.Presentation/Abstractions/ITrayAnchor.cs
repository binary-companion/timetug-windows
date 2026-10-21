namespace TimeTug.Presentation.Abstractions;

/// <summary>Which of the three ways of finding the tray icon answered, from the most to the least exact.</summary>
public enum AnchorSource
{
    /// <summary><c>Shell_NotifyIconGetRect</c>: the icon's own rectangle in the notification area.</summary>
    Icon,

    /// <summary>The icon sits in the overflow flyout; this is the overflow button (the chevron), found through UI Automation on the taskbar.</summary>
    OverflowButton,

    /// <summary>Neither worked; this is the taskbar's notification area, whose corner nearest the clock is where the flyout goes.</summary>
    NotificationArea,
}

/// <summary>Where the tray icon is on screen, in physical pixels, and how that was found.</summary>
public sealed record AnchorRect(PixelRect Rect, AnchorSource Source);

/// <summary>
/// Finds the tray icon on screen so the flyout can open next to it. Finding it can be slow (an icon in the overflow is found through
/// UI Automation, which waits on Explorer), so the UI thread uses <see cref="Peek"/>, which never waits, and lets <see cref="Refresh"/> look
/// again in the background.
/// </summary>
public interface ITrayAnchor
{
    /// <summary>
    /// The full answer: the icon's rectangle, or the best stand-in for it; null when nothing about the taskbar could be found. Never
    /// throws, but it may wait for Explorer for a couple of seconds, so it is for diagnostics and self-tests, never the UI thread.
    /// </summary>
    AnchorRect? TryGetAnchor();

    /// <summary>
    /// The answer that is available now, without waiting: the last good one from <see cref="Refresh"/>, else what the quick questions
    /// say (the icon's own rectangle, then the notification area). Null when nothing is known. Safe on the UI thread.
    /// </summary>
    AnchorRect? Peek();

    /// <summary>Starts a full look in the background and returns at once. <see cref="Changed"/> is raised when it ends with an answer different from the last one <see cref="Peek"/> gave.</summary>
    void Refresh();

    /// <summary>Forgets the last good answer (the displays, the scaling or the taskbar settings changed, so it may be wrong).</summary>
    void Invalidate();

    /// <summary>A background <see cref="Refresh"/> found a different place. Raised on any thread.</summary>
    event Action? Changed;
}
