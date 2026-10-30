using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Chip;

/// <summary>Makes the chip's window each time the chip appears.</summary>
internal sealed class ChipWindowFactory(IUiSettings settings, IPresentationSettingsStore store, ILocalizer text, IUiDispatcher dispatcher) : IChipWindowFactory
{
    /// <summary>The window made last (for the self-test).</summary>
    public ChipWindow? Last { get; private set; }

    public IChipWindow Create()
    {
        var window = new ChipWindow(settings, store, text, dispatcher);
        Last = window;
        return window;
    }
}

/// <summary>
/// What the chip's click and menu do: the flyout toggles like a click on the tray icon, Join goes through the flyout's own path (and shows the flyout, with its
/// copy-link state, when the link could not be opened), and Settings opens on the Appearance pane. The functions are the shell's, so this holds no state.
/// </summary>
internal sealed class ChipActions(Func<bool> toggleFlyout, Func<string, ValueTask<bool>> join, Action openAppearance) : IChipActions
{
    public void ToggleFlyout() => toggleFlyout();

    public async ValueTask JoinAsync(string eventId)
    {
        _ = await join(eventId);
    }

    public void OpenAppearanceSettings() => openAppearance();
}
