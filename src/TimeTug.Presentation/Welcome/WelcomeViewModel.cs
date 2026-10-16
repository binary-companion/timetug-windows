using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Welcome;

public enum WelcomeStep
{
    /// <summary>"Welcome to TimeTug": pick where to connect.</summary>
    Connect,

    /// <summary>"You're connected": what was found, with Add another, Test tug and Done.</summary>
    Connected,
}

/// <summary>The first-run window: connect a first account, then see what was found. Never shown when accounts exist.</summary>
public sealed class WelcomeViewModel : ObservableObject, IDisposable
{
    public WelcomeViewModel(IEngineClient engine, IUiDispatcher ui, ILocalizer text, TimeProvider? clock = null)
    {
        Accounts = new AccountsViewModel(engine, ui, text, clock);
        Accounts.Connected += (_, _) => Step = WelcomeStep.Connected;
        AddAnotherCommand = new RelayCommand(AddAnother);
        DoneCommand = new RelayCommand(Done);
        SkipCommand = new AsyncRelayCommand(SkipAsync);
    }

    /// <summary>Raised when the window should close (Done or Skip for now).</summary>
    public event EventHandler? CloseRequested;

    public AccountsViewModel Accounts { get; }

    public ObservableCollection<ProviderItem> Providers => Accounts.Providers;

    public WelcomeStep Step
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsConnectStep));
                OnPropertyChanged(nameof(IsConnectedStep));
            }
        }
    } = WelcomeStep.Connect;

    public bool IsConnectStep => Step == WelcomeStep.Connect;

    public bool IsConnectedStep => Step == WelcomeStep.Connected;

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

    /// <summary>Back to the list of places to connect.</summary>
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
        Accounts.ClearErrors();
        Step = WelcomeStep.Connect;
    }
}
