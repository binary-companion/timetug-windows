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

/// <summary>Finds the tray icon on screen so the flyout can open next to it.</summary>
public interface ITrayAnchor
{
    /// <summary>The icon's rectangle, or the best stand-in for it; null when nothing about the taskbar could be found. Never throws.</summary>
    AnchorRect? TryGetAnchor();
}
