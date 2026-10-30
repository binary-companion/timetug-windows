using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Chip;

/// <summary>
/// The chip's window, as <see cref="ChipHost"/> sees it. The WinUI window implements it; tests use a fake. The window is borderless, always on top, never
/// activated and absent from the taskbar and Alt+Tab; it decides its own size from its content (<see cref="MeasureContent"/>), draws the menu a right click
/// opens and reports what the person chose as events. All members are called on the UI thread.
/// </summary>
public interface IChipWindow : IDisposable
{
    /// <summary>A plain left click (not a drag).</summary>
    event Action? Clicked;

    /// <summary>"Join" was chosen in the menu.</summary>
    event Action? JoinRequested;

    /// <summary>"Hide until the next meeting" was chosen in the menu.</summary>
    event Action? HideRequested;

    /// <summary>"Open Settings" was chosen in the menu.</summary>
    event Action? SettingsRequested;

    /// <summary>The person dropped the chip after dragging it; the rectangle is where it is now, in physical pixels.</summary>
    event Action<PixelRect>? Dragged;

    /// <summary>Double click: the person wants the chip back in its default place.</summary>
    event Action? ResetRequested;

    /// <summary>The window closed, whoever closed it.</summary>
    event Action? Closed;

    /// <summary>True while the person holds the chip and moves it; the host leaves the window alone until the drop.</summary>
    bool IsDragging { get; }

    /// <summary>The size the content wants now, in DIPs (the width follows the text up to 320 DIP; the height is fixed).</summary>
    SizeInDips MeasureContent();

    /// <summary>Shows what the chip shows now (text, accent, the menu's Join item). Called before <see cref="Show"/> and on every change.</summary>
    void SetContent(ChipPlan plan);

    /// <summary>Puts the window at <paramref name="bounds"/> (physical pixels) and shows it without activating it.</summary>
    void Show(PixelRect bounds);

    /// <summary>Moves and resizes the visible window.</summary>
    void Move(PixelRect bounds);

    /// <summary>Hides the window (it stays alive, for the tug or a full-screen app that has the screen for now).</summary>
    void Hide();

    /// <summary>Has a screen reader say the sentence (a polite UI Automation notification; the window never has the keyboard).</summary>
    void Announce(string text);
}

/// <summary>Makes the chip's window.</summary>
public interface IChipWindowFactory
{
    IChipWindow Create();
}

/// <summary>What the chip's click and menu do in the rest of the app.</summary>
public interface IChipActions
{
    /// <summary>Opens the flyout, or closes it when it is open (the same call a left click on the tray icon makes).</summary>
    void ToggleFlyout();

    /// <summary>Opens the meeting's link, like the flyout's Join button. Never throws.</summary>
    ValueTask JoinAsync(string eventId);

    /// <summary>Opens the Settings window on the Appearance pane.</summary>
    void OpenAppearanceSettings();
}
