using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TimeTug.Presentation.Accounts;

/// <summary>
/// The pinned first row of the accounts list on Windows, "This PC's Windows account": the account that belongs to the device, with its switch. It is inside the
/// box, above the list rows, never selected and never removed with "-". <see cref="AccountsViewModel"/> decides every value; this holds them for the control.
/// </summary>
public sealed class DeviceRowModel : ObservableObject
{
    private readonly Func<bool, Task> _setOn;

    internal DeviceRowModel(string title, string switchAutomationName, IAsyncRelayCommand signInAgain, Func<bool, Task> setOn)
    {
        Title = title;
        SwitchAutomationName = switchAutomationName;
        SignInAgainCommand = signInAgain;
        _setOn = setOn;
    }

    /// <summary>True when Windows lists an account the broker can use. Otherwise the whole row is absent.</summary>
    public bool IsVisible
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>"This PC's Windows account".</summary>
    public string Title { get; }

    /// <summary>The listed account's email when Windows names exactly one; null otherwise.</summary>
    public string? Email
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The line under the row: what Windows will do, or why the switch is off. Empty when there is nothing to say.</summary>
    public string Description
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    /// <summary>In the welcome window the pending choice (on by default); in Settings whether the Windows account is connected.</summary>
    public bool IsOn
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public bool IsSwitchEnabled
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The connected Windows account needs the person again: the row shows "Sign in again".</summary>
    public bool NeedsSignIn
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>The connected account's status in words ("Connected", "Sign in again"); empty when it is not connected.</summary>
    public string StatusText
    {
        get;
        private set => SetProperty(ref field, value);
    } = "";

    /// <summary>"Use this PC's Windows account".</summary>
    public string SwitchAutomationName { get; }

    public IAsyncRelayCommand SignInAgainCommand { get; }

    /// <summary>The person moved the switch. A no-op while the switch is disabled (a sign-in runs, or the browser already holds the account).</summary>
    public Task SetOnAsync(bool on) => _setOn(on);

    internal void Update(bool isVisible, string? email, string description, bool isOn, bool isSwitchEnabled, bool needsSignIn, string statusText)
    {
        IsVisible = isVisible;
        Email = email;
        Description = description;
        IsOn = isOn;
        IsSwitchEnabled = isSwitchEnabled;
        NeedsSignIn = needsSignIn;
        StatusText = statusText;
        SignInAgainCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Announces <see cref="IsOn"/> again so a control the person already toggled shows the model's truth (for example after a removal question was cancelled).</summary>
    internal void Resync() => OnPropertyChanged(nameof(IsOn));
}
