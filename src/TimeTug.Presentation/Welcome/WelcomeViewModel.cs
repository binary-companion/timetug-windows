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

    /// <summary>"Welcome to TimeTug" with Get started. The Windows account listing runs while it is up.</summary>
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
        Accounts = new AccountsViewModel(engine, ui, text, clock) { ShowWindowsOfferRow = true };
        Accounts.Connected += (_, _) => OnAccountConnected();
        Accounts.PropertyChanged += OnAccountsChanged;
        Accounts.ListAccounts.CollectionChanged += (_, _) => RefreshContinue();
        GetStartedCommand = new RelayCommand(GetStarted, () => Step == WelcomeStep.Intro);
        ContinueCommand = new AsyncRelayCommand(ContinueAsync, () => CanContinue);
        AddAnotherCommand = new RelayCommand(AddAnother);
        DoneCommand = new RelayCommand(Done);
        SkipCommand = new AsyncRelayCommand(SkipAsync);
    }

    /// <summary>Raised when the window should close (Done or Skip for now).</summary>
    public event EventHandler? CloseRequested;

    public AccountsViewModel Accounts { get; }

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
    } = WelcomeStep.Intro;

    public bool IsConnectStep => Step == WelcomeStep.Connect;

    public bool IsConnectedStep => Step == WelcomeStep.Connected;

    public bool IsIntroStep => Step == WelcomeStep.Intro;

    public string GetStartedText => Accounts.Text("Welcome_GetStarted");

    public string ContinueText => Accounts.Text("Welcome_Continue");

    /// <summary>"Get started": from the intro to the connect step.</summary>
    public IRelayCommand GetStartedCommand { get; }

    /// <summary>
    /// "Continue" on the connect step. When Windows lists a Microsoft account that is not connected and was not removed before (<see cref="AccountsViewModel.WindowsAccountOffered"/>)
    /// its row is already in the box; Continue connects it as an ordinary account: Windows' own consent dialog is the first and only request to Windows. Success goes to the connected step; declining,
    /// closing or failing the dialog stays here, and a second Continue with an account in the list goes on without asking again. With nothing offered it goes on and
    /// asks Windows for nothing.
    /// </summary>
    public IAsyncRelayCommand ContinueCommand { get; }

    /// <summary>
    /// Continue can be pressed: no sign-in runs, the Windows listing has answered, and an account is connected or the Windows account is offered (its row is in the
    /// box). False while the listing is pending, with nothing offered and nothing connected, and outside the connect step.
    /// </summary>
    public bool CanContinue => Step == WelcomeStep.Connect && !Accounts.IsBusy
        && !Accounts.ListingPending && (Accounts.WindowsAccountOffered || Accounts.ConnectedAccounts.Count > 0);

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

        // With the Windows account's row still in the box, Continue asks Windows; after it was declined, closed or failed it asks again while nothing else is connected,
        // and goes on without it once another account is (the row says so).
        if (Accounts.WindowsAccountOffered && !(Accounts.WindowsOfferDeclined && Accounts.ConnectedAccounts.Count > 0))
        {
            _continuing = true;
            await Accounts.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
            if (!Accounts.IsBusy)
            {
                _continuing = false;   // nothing started, or it already ended without an account: the row carries the status
            }

            return;
        }

        Step = WelcomeStep.Connected;
    }

    private void OnAccountConnected()
    {
        // An account added with "+" never moves the window on; only Continue does.
        if (_continuing)
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
        else if (e.PropertyName is nameof(AccountsViewModel.WindowsAccountOffered) or nameof(AccountsViewModel.ListingPending))
        {
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
