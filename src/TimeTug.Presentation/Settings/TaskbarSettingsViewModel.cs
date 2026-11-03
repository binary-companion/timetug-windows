using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// "Show TimeTug in the taskbar": opens the Taskbar page of Windows Settings, where TimeTug can be turned on under "Other system tray icons".
/// Windows puts a new tray icon behind the arrow next to the clock and offers an app no supported way to promote itself, so the app can only
/// send the person to the setting. Used by the last step of the welcome window and by the General pane. A link that does not open is logged
/// (no content, only that it failed) and shown as a short message; nothing throws.
/// </summary>
public sealed class TaskbarSettingsViewModel : ObservableObject
{
    /// <summary>The Taskbar page of Windows Settings (Personalization, Taskbar).</summary>
    public const string SettingsUri = "ms-settings:taskbar";

    private const string Category = "taskbar";

    private readonly ISystemLauncher _launcher;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;
    private readonly IDiagnostics? _diagnostics;

    public TaskbarSettingsViewModel(ISystemLauncher launcher, IUiDispatcher ui, ILocalizer text, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        _launcher = launcher;
        _ui = ui;
        _text = text;
        _diagnostics = diagnostics;
    }

    /// <summary>The welcome window's button and the General pane's button: "Show TimeTug in the taskbar".</summary>
    public string ButtonText => _text.GetString("Taskbar_Button");

    /// <summary>What a screen reader says for the button.</summary>
    public string ButtonAutomationName => _text.GetString("Taskbar_ButtonAutomation");

    /// <summary>Why the page did not open, or null.</summary>
    public string? Error
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => Error is not null;

    /// <summary>Opens the Taskbar page of Windows Settings. Never throws: a failure is logged and shown as <see cref="Error"/>.</summary>
    public async Task OpenAsync()
    {
        var opened = false;
        try
        {
            opened = await _launcher.LaunchAsync(SettingsUri);
            if (!opened)
            {
                _diagnostics?.Record("warning", Category, "The Taskbar settings page did not open.");
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _diagnostics?.Record("warning", Category, "Opening the Taskbar settings failed: " + e.GetType().Name);
        }

        await _ui.InvokeAsync(() => Error = opened ? null : _text.GetString("Taskbar_OpenFailed"));
    }
}

/// <summary>The animated guide the welcome window shows (a recording of Windows Settings, Taskbar, turning TimeTug on) and the still it shows instead when Windows has animations off.</summary>
public static class TaskbarGuide
{
    public const string AnimatedAsset = "Assets/taskbar-guide.gif";

    /// <summary>The last frame of the animation: TimeTug turned on.</summary>
    public const string StillAsset = "Assets/taskbar-guide-still.png";

    /// <summary>The picture to show: the looping animation, or a still of its last frame when the person has turned animations off in Windows.</summary>
    public static string AssetFor(bool animationsEnabled) => animationsEnabled ? AnimatedAsset : StillAsset;
}
