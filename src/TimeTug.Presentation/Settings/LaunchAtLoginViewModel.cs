using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Startup;

namespace TimeTug.Presentation.Settings;

/// <summary>
/// Launch at login (the Mac's "Launch at login" toggle) over the Windows startup task. Windows is the source of truth and the app keeps
/// no copy: every <see cref="RefreshAsync"/> reads the state again, and a request is shown as what Windows says afterwards, so the switch
/// never claims "on" when Windows has the task off (the person turned it off in Settings or Task Manager, or a policy did). A task the
/// person or a policy has turned off cannot be turned on from here: the switch is disabled, the cause is explained and a link opens the
/// Startup apps page of Windows Settings. Nothing is persisted.
/// </summary>
public sealed class LaunchAtLoginViewModel : ObservableObject
{
    /// <summary>The Startup apps page of Windows Settings.</summary>
    public const string StartupSettingsUri = "ms-settings:startupapps";

    private readonly IStartupTaskService _startup;
    private readonly ISystemLauncher _launcher;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;

    public LaunchAtLoginViewModel(IStartupTaskService startup, ISystemLauncher launcher, IUiDispatcher ui, ILocalizer text)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(text);
        _startup = startup;
        _launcher = launcher;
        _ui = ui;
        _text = text;
    }

    /// <summary>What Windows reported last; null until it has been asked, or when it could not be read.</summary>
    public StartupState? State
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsOn));
                OnPropertyChanged(nameof(CanChange));
                OnPropertyChanged(nameof(ShowSettingsLink));
                OnPropertyChanged(nameof(Note));
                OnPropertyChanged(nameof(HasNote));
            }
        }
    }

    /// <summary>The switch: on only while Windows has the task on.</summary>
    public bool IsOn => State is StartupState.Enabled or StartupState.EnabledByPolicy;

    /// <summary>The person can turn it on or off from here: only for a task that is plainly on or off, and not while a request is pending.</summary>
    public bool CanChange => !IsBusy && State is StartupState.Enabled or StartupState.Disabled;

    /// <summary>A request to Windows is pending.</summary>
    public bool IsBusy
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(CanChange));
            }
        }
    }

    /// <summary>Show the link to Windows Settings: the person or a policy has the task off.</summary>
    public bool ShowSettingsLink => State is StartupState.DisabledByUser or StartupState.DisabledByPolicy;

    public string SettingsLinkText => _text.GetString("LaunchAtLogin_OpenStartupApps");

    public string SettingsLinkAutomationName => _text.GetString("LaunchAtLogin_OpenStartupAppsAutomation");

    public string AutomationName => _text.GetString("LaunchAtLogin_Automation");

    /// <summary>Why the switch cannot be used, when Windows or a policy decides.</summary>
    public string? Note => State switch
    {
        StartupState.DisabledByUser => _text.GetString("LaunchAtLogin_DisabledByUser"),
        StartupState.DisabledByPolicy => _text.GetString("LaunchAtLogin_DisabledByPolicy"),
        StartupState.EnabledByPolicy => _text.GetString("LaunchAtLogin_EnabledByPolicy"),
        _ => null,
    };

    public bool HasNote => Note is not null;

    /// <summary>Why the last request or read failed, or null (the Mac's "Couldn't change launch at login: ...").</summary>
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

    /// <summary>Reads the state from Windows. Call it whenever the page appears and whenever the window becomes active again, since the person may have changed it in Windows Settings meanwhile.</summary>
    public async Task RefreshAsync()
    {
        try
        {
            var state = await _startup.GetStateAsync();
            await _ui.InvokeAsync(() =>
            {
                State = state;
                Error = null;
            });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await _ui.InvokeAsync(() =>
            {
                State = null;
                Error = _text.Format("LaunchAtLogin_Unreadable", e.Message);
            });
        }
    }

    /// <summary>The person turned the switch on or off. Windows is asked and the switch shows what Windows says afterwards.</summary>
    public async Task SetAsync(bool on)
    {
        if (!CanChange)
        {
            // The switch was disabled or busy: nothing to ask, and the property is raised again so the control goes back to what is true.
            OnPropertyChanged(nameof(IsOn));
            return;
        }

        if (on == IsOn)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var state = on ? await _startup.RequestEnableAsync() : await _startup.DisableAsync();
            var error = Failure(on, state);
            await _ui.InvokeAsync(() =>
            {
                State = state;
                Error = error;
                OnPropertyChanged(nameof(IsOn));
            });
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await _ui.InvokeAsync(() => Error = _text.Format("LaunchAtLogin_Failed", e.Message));
            await RefreshKeepingErrorAsync();
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsOn));
        }
    }

    /// <summary>Opens the Startup apps page of Windows Settings.</summary>
    public async Task OpenStartupSettingsAsync()
    {
        var opened = await _launcher.LaunchAsync(StartupSettingsUri);
        await _ui.InvokeAsync(() => Error = opened ? null : _text.GetString("LaunchAtLogin_LinkFailed"));
    }

    private string? Failure(bool wanted, StartupState state)
    {
        if (wanted == IsOnIn(state))
        {
            return null;
        }

        // Windows left it as it was. The person or a policy having turned it off is explained by the note; any other case is said plainly.
        return state is StartupState.DisabledByUser or StartupState.DisabledByPolicy or StartupState.EnabledByPolicy
            ? null
            : _text.GetString(wanted ? "LaunchAtLogin_NotTurnedOn" : "LaunchAtLogin_NotTurnedOff");
    }

    private static bool IsOnIn(StartupState state) => state is StartupState.Enabled or StartupState.EnabledByPolicy;

    /// <summary>Reads the real state after a failed request, without wiping the message that says why it failed.</summary>
    private async Task RefreshKeepingErrorAsync()
    {
        try
        {
            var state = await _startup.GetStateAsync();
            await _ui.InvokeAsync(() => State = state);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await _ui.InvokeAsync(() => State = null);
        }
    }
}
