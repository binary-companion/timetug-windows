using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Settings;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Accounts;

/// <summary>A scripted Windows account listing that records every question put to it (the only broker call allowed before Continue).</summary>
internal sealed class FakeListing(WindowsAccountListing? answer = null) : IWindowsAccountSource
{
    public List<string> Calls { get; } = [];

    /// <summary>When set, the listing answers only when the test completes it (a slow broker).</summary>
    public TaskCompletionSource<WindowsAccountListing>? Gate { get; set; }

    public ValueTask<WindowsAccountListing> GetListingAsync(CancellationToken cancellationToken)
    {
        Calls.Add("listing");
        return Gate is { } gate ? new ValueTask<WindowsAccountListing>(gate.Task) : ValueTask.FromResult(answer ?? new WindowsAccountListing(WindowsListingKind.None));
    }
}

/// <summary>An in-memory dismissal store that counts the writes.</summary>
internal sealed class FakeDismissals(bool dismissed = false) : IWindowsAccountDismissalStore
{
    public bool IsDismissed { get; private set; } = dismissed;

    public int DismissCalls { get; private set; }

    public void Dismiss()
    {
        DismissCalls++;
        IsDismissed = true;
    }
}

/// <summary>An <see cref="AccountsViewModel"/> over a scripted engine, a recording intent and a scripted Windows listing.</summary>
internal sealed class ListRig
{
    public static readonly WindowsAccountListing Named = new(WindowsListingKind.OneNamed, "me@example.com");
    public static readonly WindowsAccountListing Unnamed = new(WindowsListingKind.Unnamed);
    public static readonly WindowsAccountListing None = new(WindowsListingKind.None);
    public static readonly WindowsAccountListing Unavailable = new(WindowsListingKind.Unavailable);

    public static readonly AccountActivity Idle = new(false, null, "browser");
    public static readonly AccountActivity Working = new(true, null, "browser");

    public static IReadOnlyList<AccountKindInfo> Kinds => AccountsBrokeredTests.KindsWithBrokered;

    private ListRig(FakeEngineClient engine, AccountsViewModel model, AccountsBrokeredTests.FakeIntent intent, FakeListing source, FakeDismissals dismissals)
    {
        Dismissals = dismissals;
        Engine = engine;
        Model = model;
        Intent = intent;
        Source = source;
    }

    public FakeEngineClient Engine { get; }

    public AccountsViewModel Model { get; }

    public AccountsBrokeredTests.FakeIntent Intent { get; }

    public FakeListing Source { get; }

    public FakeDismissals Dismissals { get; }

    /// <param name="listing">What Windows lists; null leaves the model without any Windows account source.</param>
    /// <param name="gated">True holds the listing until <see cref="FakeListing.Gate"/> is completed.</param>
    public static ListRig Create(WindowsAccountListing? listing = null, AppState? first = null, bool gated = false, bool withSource = true, bool dismissed = false)
    {
        var engine = new FakeEngineClient();
        var model = new AccountsViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), TimeProvider.System);
        var dismissals = new FakeDismissals(dismissed);
        model.Dismissals = dismissals;
        var intent = new AccountsBrokeredTests.FakeIntent(engine.Sent);
        model.BrokerIntents = intent;
        var source = new FakeListing(listing);
        if (gated)
        {
            source.Gate = new TaskCompletionSource<WindowsAccountListing>();
        }

        engine.Push(first ?? States.Empty(Kinds));
        if (withSource)
        {
            model.WindowsAccounts = source;
        }

        return new ListRig(engine, model, intent, source, dismissals);
    }

    public static AccountInfo Account(string id, string kind, string name, string status = "ok") => new(id, kind, name, status, 2);

    public static AppState With(AccountActivity? activity = null, params AccountInfo[] accounts) => States.With(accounts, activity: activity, kinds: Kinds);

    public static AppState Empty(AccountActivity? activity = null) => States.Empty(Kinds, activity);

    public void Push(params AccountInfo[] accounts) => Engine.Push(With(Idle, accounts));

    public void PushActivity(AccountActivity activity, params AccountInfo[] accounts) => Engine.Push(With(activity, accounts));

    /// <summary>Waits (up to two seconds) for an effect of a task the view model started and did not hand back, such as a late listing.</summary>
    public static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }
    }

    public static Task Press(ICommand? command) => ((IAsyncRelayCommand)command!).ExecuteAsync(null);
}
