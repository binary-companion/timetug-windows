using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Welcome;

public enum WelcomeStep
{
    /// <summary>"Welcome to TimeTug": pick where to connect.</summary>
    Connect,

    /// <summary>"You're connected": what was found, with Add another, Test tug and Done.</summary>
    Connected,

    /// <summary>"Welcome to TimeTug" with Get started (only in the list flow). The Windows account listing runs while it is up.</summary>
    Intro,
}

/// <summary>The first-run window: connect a first account, then see what was found. Never shown when accounts exist.</summary>
public sealed class WelcomeViewModel : ObservableObject, IDisposable
{
    private readonly Action? _openAccountsPane;
    private bool _continuing;

    /// <param name="openAccountsPane">
    /// Opens the Settings window on its Accounts pane. When the app gives one, "Add another" goes there (and this window closes) instead of
    /// showing the list of places to connect again; without one, "Add another" shows that list.
    /// </param>
    /// <param name="launchAtLogin">
    /// Launch at login, for the "Start TimeTug when I sign in" checkbox on the second step. Without one the step has no checkbox.
    /// </param>
    public WelcomeViewModel(
        IEngineClient engine, IUiDispatcher ui, ILocalizer text, TimeProvider? clock = null, Action? openAccountsPane = null,
        LaunchAtLoginViewModel? launchAtLogin = null)
    {
        _openAccountsPane = openAccountsPane;
        LaunchAtLogin = launchAtLogin;
        Accounts = new AccountsViewModel(engine, ui, text, clock) { PreselectBrokered = true };
        Accounts.Connected += (_, _) => OnAccountConnected();
        Accounts.PropertyChanged += OnAccountsChanged;
        Accounts.DeviceRow.PropertyChanged += (_, _) => RefreshContinue();
        Accounts.ListAccounts.CollectionChanged += (_, _) => RefreshContinue();
        GetStartedCommand = new RelayCommand(GetStarted, () => Step == WelcomeStep.Intro);
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => CanContinue);
        AddAnotherCommand = new RelayCommand(AddAnother);
        DoneCommand = new RelayCommand(Done);
        SkipCommand = new AsyncRelayCommand(SkipAsync);
    }

    /// <summary>
    /// The list shape of the window (shared accounts list): it starts on <see cref="WelcomeStep.Intro"/>, shows the accounts list with the pinned Windows row on the
    /// connect step, an account added with "+" never moves the window on, and only Continue does. Set by the app when it builds the window; the old shape stays until
    /// the window's XAML is switched over.
    /// </summary>
    public bool ListFlow
    {
        get;
        init
        {
            field = value;
            if (value)
            {
                Step = WelcomeStep.Intro;
                Accounts.DeviceRowIsPending = true;
                Accounts.PreselectBrokered = false;
            }
        }
    }

    /// <summary>Raised when the window should close (Done or Skip for now).</summary>
    public event EventHandler? CloseRequested;

    public AccountsViewModel Accounts { get; }

    public ObservableCollection<ProviderItem> Providers => Accounts.Providers;

    /// <summary>Launch at login, shown as a checkbox on the second step; null when the app gives none.</summary>
    public LaunchAtLoginViewModel? LaunchAtLogin { get; }

    /// <summary>The checkbox is on screen: the second step, with a launch-at-login model.</summary>
    public bool ShowLaunchAtLogin => LaunchAtLogin is not null && IsConnectedStep;

    /// <summary>The person ticked or unticked the checkbox. A tick asks Windows to enable the startup task; the checkbox then shows what Windows says.</summary>
    public Task SetLaunchAtLoginAsync(bool on) => LaunchAtLogin?.SetAsync(on) ?? Task.CompletedTask;

    public WelcomeStep Step
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsConnectStep));
                OnPropertyChanged(nameof(IsConnectedStep));
                OnPropertyChanged(nameof(IsIntroStep));
                GetStartedCommand.NotifyCanExecuteChanged();
                RefreshContinue();
                OnPropertyChanged(nameof(ShowLaunchAtLogin));
                if (value == WelcomeStep.Connected && LaunchAtLogin is { } launch)
                {
                    // What Windows has right now, which is "off" on a first run: the checkbox is read here, not assumed.
                    _ = launch.RefreshAsync();
                }
            }
        }
    } = WelcomeStep.Connect;

    public bool IsConnectStep => Step == WelcomeStep.Connect;

    public bool IsConnectedStep => Step == WelcomeStep.Connected;

    public bool IsIntroStep => Step == WelcomeStep.Intro;

    public string GetStartedText => Accounts.Text("Welcome_GetStarted");

    public string ContinueText => Accounts.Text("Welcome_Continue");

    /// <summary>"Get started": from the intro to the connect step.</summary>
    public IRelayCommand GetStartedCommand { get; }

    /// <summary>
    /// "Continue" on the connect step. With the Windows row's switch on and the account not yet connected it asks Windows (the first time anything is requested from
    /// Windows); success goes to the connected step, and declining, closing or failing the dialog stays here with the switch still on. With the Windows account
    /// connected already, or the switch off and an account in the list, it goes on and asks Windows for nothing.
    /// </summary>
    public IAsyncRelayCommand ContinueCommand { get; }

    /// <summary>
    /// Continue can be pressed: no sign-in runs, and the Windows row is shown with its switch on or the list has an account. False while the listing is pending with an
    /// empty list, with the switch off and an empty list, and outside the connect step.
    /// </summary>
    public bool CanContinue => ListFlow && Step == WelcomeStep.Connect && !Accounts.IsBusy
        && (Accounts.DeviceRow is { IsVisible: true, IsOn: true } || Accounts.ListAccounts.Count > 0);

    public IRelayCommand AddAnotherCommand { get; }

    public IRelayCommand DoneCommand { get; }

    public IAsyncRelayCommand SkipCommand { get; }

    /// <summary>True only when the engine's first state has no accounts. The window never appears for an existing install.</summary>
    public static bool ShouldShowAtStartup(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Accounts.Count == 0;
    }

    public Task ConnectAsync(string kindId) => Accounts.ConnectAsync(kindId);

    private void GetStarted()
    {
        if (Step == WelcomeStep.Intro)
        {
            Step = WelcomeStep.Connect;
        }
    }

    private async Task ContinueAsync()
    {
        if (!CanContinue)
        {
            return;
        }

        if (Accounts.DeviceRow is { IsVisible: true, IsOn: true } && !Accounts.WindowsAccountConnected)
        {
            _continuing = true;
            await Accounts.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
            if (!Accounts.IsBusy)
            {
                _continuing = false;   // nothing started, or it already ended
            }

            return;
        }

        Step = WelcomeStep.Connected;
    }

    private void OnAccountConnected()
    {
        if (!ListFlow)
        {
            Step = WelcomeStep.Connected;
        }
        else if (_continuing)
        {
            _continuing = false;
            Step = WelcomeStep.Connected;
        }
    }

    private void OnAccountsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AccountsViewModel.IsBusy))
        {
            if (_continuing && !Accounts.IsBusy)
            {
                _continuing = false;   // the Windows dialog was declined, closed or failed: stay on this step
            }

            RefreshContinue();
        }
    }

    private void RefreshContinue()
    {
        OnPropertyChanged(nameof(CanContinue));
        ContinueCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Back to the list of places to connect, or to the Settings window's Accounts pane when the app provides one.</summary>
    public Task AddAnotherAsync()
    {
        AddAnother();
        return Task.CompletedTask;
    }

    public Task TestTugAsync() => Accounts.TestTugAsync();

    public void Done() => CloseRequested?.Invoke(this, EventArgs.Empty);

    public async Task SkipAsync()
    {
        await Accounts.CancelAsync();
        Done();
    }

    public void Dispose() => Accounts.Dispose();

    private void AddAnother()
    {
        if (_openAccountsPane is { } open)
        {
            open();
            Done();
            return;
        }

        Accounts.ClearErrors();
        Step = WelcomeStep.Connect;
    }
}
