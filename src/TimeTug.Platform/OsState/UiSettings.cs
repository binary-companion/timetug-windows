using Microsoft.Win32;
using TimeTug.Presentation.Abstractions;
using Windows.UI.ViewManagement;

namespace TimeTug.Platform.OsState;

/// <summary>The appearance settings the UI follows, as one comparable value.</summary>
public readonly record struct UiSnapshot(bool AnimationsEnabled, bool TransparencyEffectsEnabled, bool HighContrast, bool TaskbarUsesLightTheme, bool AppsUseDarkTheme = false);

/// <summary>
/// Animations ("Animation effects"), transparency effects, high contrast, the taskbar theme and the apps theme, read live from
/// Windows. <see cref="Changed"/> is raised when one of the five values differs from the last notification.
/// </summary>
public sealed class UiSettings : IUiSettings, IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private readonly Func<UiSnapshot> _read;
    private readonly IWindowMessageSource? _messages;
    private readonly Windows.UI.ViewManagement.UISettings? _uiSettings;
    private readonly AccessibilitySettings? _accessibility;
    private readonly object _gate = new();
    private readonly bool _highContrastEvent;
    private UiSnapshot _last;

    /// <param name="messages">Optional: <c>WM_SETTINGCHANGE</c> is how a change of "Animation effects" reaches an app (UISettings has no event for it).</param>
    /// <param name="read">Reads the four values; defaults to the real settings, in which case the WinRT change events are subscribed as well.</param>
    public UiSettings(IWindowMessageSource? messages = null, Func<UiSnapshot>? read = null, IDiagnostics? diagnostics = null)
    {
        _messages = messages;
        if (read is null)
        {
            _uiSettings = new Windows.UI.ViewManagement.UISettings();
            _accessibility = new AccessibilitySettings();
            _uiSettings.ColorValuesChanged += OnPlatformChanged;
            _uiSettings.AdvancedEffectsEnabledChanged += OnPlatformChanged;
            try
            {
                _accessibility.HighContrastChanged += OnPlatformChanged;
                _highContrastEvent = true;
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                // AccessibilitySettings raises its event only for objects made on a thread with a window; the settings
                // message (WM_SETTINGCHANGE) reports the same change, so this is not fatal.
                diagnostics?.Record("info", "ui", "the high contrast change event is not available on this thread; relying on settings messages");
            }
        }

        _read = read ?? ReadWindows;
        _last = _read();
        if (messages is not null)
        {
            messages.MessageReceived += OnMessage;
        }
    }

    public bool AnimationsEnabled => _read().AnimationsEnabled;

    public bool TransparencyEffectsEnabled => _read().TransparencyEffectsEnabled;

    public bool HighContrast => _read().HighContrast;

    public bool TaskbarUsesLightTheme => _read().TaskbarUsesLightTheme;

    public bool AppsUseDarkTheme => _read().AppsUseDarkTheme;

    public event Action? Changed;

    /// <summary>Re-reads the settings and raises <see cref="Changed"/> when they differ from the last notification.</summary>
    public void Refresh()
    {
        var now = _read();
        UiSnapshot before;
        lock (_gate)
        {
            before = _last;
            _last = now;
        }

        if (before != now)
        {
            Changed?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_messages is not null)
        {
            _messages.MessageReceived -= OnMessage;
        }

        if (_uiSettings is not null)
        {
            _uiSettings.ColorValuesChanged -= OnPlatformChanged;
            _uiSettings.AdvancedEffectsEnabledChanged -= OnPlatformChanged;
        }

        if (_accessibility is not null && _highContrastEvent)
        {
            _accessibility.HighContrastChanged -= OnPlatformChanged;
        }
    }

    private void OnMessage(WindowMessage message)
    {
        if (message.Id == WindowMessage.SettingChange)
        {
            Refresh();
        }
    }

    private void OnPlatformChanged(object? sender, object? args) => Refresh();

    private UiSnapshot ReadWindows() => new(
        _uiSettings!.AnimationsEnabled,
        _uiSettings.AdvancedEffectsEnabled,
        _accessibility!.HighContrast,
        ReadTaskbarUsesLightTheme(),
        ReadAppsUseDarkTheme());

    /// <summary>The registry value apps follow ("Choose your default app mode"); light when Windows has not written one.</summary>
    private static bool ReadAppsUseDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    /// <summary>The registry value the taskbar and tray follow; light when Windows has not written one.</summary>
    private static bool ReadTaskbarUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("SystemUsesLightTheme") is not int value || value != 0;
    }
}
