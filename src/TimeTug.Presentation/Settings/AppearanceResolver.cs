using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// Turns the person's appearance choices and the Windows settings into what a window should show. Pure: the windows, the flyout host and the
/// Appearance pane all ask here, so they cannot disagree.
/// </summary>
public static class AppearanceResolver
{
    /// <summary>
    /// Whether the app's own windows are dark: the choice when it is Light or Dark, the apps theme Windows reports when it is "Use system".
    /// (A contrast theme is separate: the window layer leaves the colours to Windows then, and this is only used for the parts it draws itself.)
    /// </summary>
    public static bool IsDark(AppTheme theme, IUiSettings ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        return theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => ui.AppsUseDarkTheme,
        };
    }

    /// <summary>The flyout's backdrop: the choice, unless transparency effects are off or a contrast theme is on, which always give the solid one.</summary>
    public static FlyoutBackdrop Backdrop(FlyoutBackdrop chosen, IUiSettings ui) => BackdropOverridden(ui) ? FlyoutBackdrop.Solid : chosen;

    /// <summary>True when Windows (reduced transparency or a contrast theme) decides the flyout's backdrop instead of the person.</summary>
    public static bool BackdropOverridden(IUiSettings ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        return ui.HighContrast || !ui.TransparencyEffectsEnabled;
    }
}
