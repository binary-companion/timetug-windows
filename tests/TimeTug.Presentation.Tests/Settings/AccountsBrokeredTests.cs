using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>The brokered Microsoft kind in the accounts view model: intents, waiting text, reconnect, the "On this PC" tag, the withdraw-consent text and removal. (The menu entry, the pinned row and the fallback link are in the <c>Accounts</c> tests.)</summary>
public class AccountsBrokeredTests
{
    internal static readonly AccountActivity Working = new(true, null, "browser");

    internal static readonly IReadOnlyList<AccountKindInfo> KindsWithBrokered =
    [
        new("google", "Google", true, "oauth"),
        new("microsoft", "Microsoft", true, "oauth"),
        new("microsoft-brokered", "Microsoft", true, "oauth"),
        new("icloud", "iCloud", true, "password"),
        new("caldav", "Other (CalDAV)", true, "password"),
    ];

    internal sealed class FakeIntent(List<string> engineSent) : IBrokerSignInIntent
    {
        public List<(BrokerIntent Intent, int EngineCommandsBefore)> Armed { get; } = [];

        public BrokerIntent Current { get; private set; } = BrokerIntent.WindowsAccount;

        public void Arm(BrokerIntent intent)
        {
            Armed.Add((intent, engineSent.Count));
            Current = intent;
        }

        public BrokerIntent Take()
        {
            var taken = Current;
            Current = BrokerIntent.WindowsAccount;
            return taken;
        }
    }

    internal sealed class FakeWindowsAccounts(string? email) : IWindowsAccountSource
    {
        public int Asked { get; private set; }

        public ValueTask<WindowsAccountListing> GetListingAsync(CancellationToken cancellationToken)
        {
            Asked++;
            return ValueTask.FromResult(email is null ? new WindowsAccountListing(WindowsListingKind.None) : new WindowsAccountListing(WindowsListingKind.OneNamed, email));
        }
    }

    private static (FakeEngineClient Engine, AccountsViewModel Model, FakeIntent Intent) Create(AppState? first = null)
    {
        var engine = new FakeEngineClient();
        var model = new AccountsViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), TimeProvider.System);
        var intent = new FakeIntent(engine.Sent);
        model.BrokerIntents = intent;
        engine.Push(first ?? States.Empty(KindsWithBrokered));
        return (engine, model, intent);
    }

    private static AccountInfo Account(string id, string kind, string name, string status = "ok") => new(id, kind, name, status, 2);

    private static AppState With(AccountActivity? activity = null, params AccountInfo[] accounts) => States.With(accounts, activity: activity, kinds: KindsWithBrokered);

    private static AppState Idle(string? error = null) => States.Empty(KindsWithBrokered, new AccountActivity(false, error, "browser"));

    // ---- the list -----------------------------------------------------------------------------------------------------------------

    // ---- intents ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Connect_brokered_arms_the_given_intent_then_adds()
    {
        var (engine, model, intent) = Create();

        await model.ConnectBrokeredAsync(BrokerIntent.PickAccount);

        intent.Armed.Should().Equal((BrokerIntent.PickAccount, 0));
        engine.Sent.Should().Equal("add:microsoft-brokered");
    }

    [Fact]
    public async Task Connecting_by_the_item_ids_works_like_the_buttons()
    {
        var (engine, model, intent) = Create();

        await model.ConnectAsync("microsoft-brokered:pick");

        intent.Armed.Should().Equal((BrokerIntent.PickAccount, 0));
        engine.Sent.Should().Equal("add:microsoft-brokered");
    }

    [Fact]
    public async Task The_intent_is_reset_when_an_attempt_ends_without_reaching_the_broker()
    {
        var (engine, model, intent) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.PickAccount);
        intent.Current.Should().Be(BrokerIntent.PickAccount);

        // The engine fails before the host takes the intent (for example the command was refused).
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));
        engine.Push(Idle("Sign-in failed: something."));

        intent.Current.Should().Be(BrokerIntent.WindowsAccount);
        intent.Armed.Last().Intent.Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task Cancelling_an_attempt_resets_the_intent()
    {
        var (_, model, intent) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.PickAccount);

        await model.CancelAsync();

        intent.Current.Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task A_successful_attempt_resets_the_intent()
    {
        var (engine, model, intent) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.PickAccount);

        engine.Push(With(null, Account("b", "microsoft-brokered", "me@example.com")));

        intent.Current.Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task Reconnect_after_a_picker_attempt_uses_the_windows_account_intent_armed_before_the_command()
    {
        var expired = Account("b", "microsoft-brokered", "me@example.com", "authExpired");
        var (engine, model, intent) = Create(With(null, expired));
        await model.ConnectBrokeredAsync(BrokerIntent.PickAccount);
        await model.CancelAsync();
        intent.Armed.Clear();
        engine.Sent.Clear();

        await model.ReauthorizeAsync("b");

        intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0)); // armed before 'reconnect' is sent
        engine.Sent.Should().Equal("reconnect:b");
    }

    // ---- waiting ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_waiting_text_of_a_brokered_attempt_is_windows_whatever_the_engine_says()
    {
        var (engine, model, _) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));

        model.IsBusy.Should().BeTrue();
        model.WaitingText.Should().Be("Waiting for Windows…");
    }

    [Fact]
    public async Task The_waiting_text_of_a_browser_attempt_is_still_the_browser()
    {
        var (_, model, _) = Create();
        await model.ConnectAsync("google");

        model.WaitingText.Should().Be("Waiting for your browser…");
    }

    [Fact]
    public async Task Reconnecting_a_brokered_account_sends_reconnect_and_waits_for_windows_until_the_engine_is_done()
    {
        var expired = Account("b", "microsoft-brokered", "me@example.com", "authExpired");
        var (engine, model, _) = Create(With(null, expired));

        await model.ReauthorizeAsync("b");
        engine.Push(With(new AccountActivity(true, null, "browser"), expired));

        engine.Sent.Should().Equal("reconnect:b");
        model.IsBusy.Should().BeTrue();
        model.WaitingText.Should().Be("Waiting for Windows…");

        engine.Push(With(null, Account("b", "microsoft-brokered", "me@example.com")));
        model.IsBusy.Should().BeFalse();
        model.WaitingText.Should().BeEmpty();
    }

    [Fact]
    public async Task A_brokered_reconnect_that_fails_with_no_working_state_does_not_mislabel_the_next_sign_in()
    {
        var expired = Account("b", "microsoft-brokered", "me@example.com", "authExpired");
        var (engine, model, _) = Create(With(null, expired));
        await model.ReauthorizeAsync("b");

        // The engine refuses at once: an error, the account still needs the person, and no working state was ever published.
        engine.Push(With(new AccountActivity(false, "Sign-in failed: Windows sign-in is not available.", "browser"), expired));
        await model.ConnectAsync("google");

        model.WaitingText.Should().Be("Waiting for your browser…");
    }

    [Fact]
    public async Task Cancelling_a_windows_sign_in_keeps_waiting_for_windows_until_the_engine_is_done()
    {
        var (engine, model, _) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));

        await model.CancelAsync();
        model.IsBusy.Should().BeTrue("the engine has not said it stopped");
        model.WaitingText.Should().Be("Waiting for Windows…");

        engine.Push(Idle());
        model.IsBusy.Should().BeFalse();
        await model.ConnectAsync("google");
        model.WaitingText.Should().Be("Waiting for your browser…");
    }

    [Fact]
    public async Task A_windows_account_source_that_throws_cancellation_does_not_fault_the_unobserved_lookup()
    {
        var (_, model, _) = Create();
        model.WindowsAccounts = new CancelledAccounts();

        await model.RefreshWindowsAccountAsync();

        model.DeviceRow.IsVisible.Should().BeFalse("a listing that was cancelled hides the row and faults nothing");
    }

    private sealed class CancelledAccounts : IWindowsAccountSource
    {
        public ValueTask<WindowsAccountListing> GetListingAsync(CancellationToken cancellationToken) => throw new OperationCanceledException();
    }

    // ---- connected rows -----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_connected_brokered_row_shows_the_on_this_pc_tag_and_a_browser_row_does_not()
    {
        var (_, model, _) = Create(With(null, Account("a", "microsoft-brokered", "me@example.com"), Account("b", "microsoft", "other@example.com")));

        model.ConnectedAccounts.Select(a => a.IsOnThisPc).Should().Equal(true, false);
        model.OnThisPcText.Should().Be("On this PC");
        model.ConnectedAccounts.Select(a => a.KindName).Should().Equal("Microsoft", "Microsoft");
        model.ConnectedAccounts[0].AutomationName.Should().Be("Microsoft, me@example.com, Connected, On this PC");
        model.ConnectedAccounts[1].AutomationName.Should().Be("Microsoft, other@example.com, Connected");
    }

    [Fact]
    public void A_brokered_account_that_needs_the_person_still_offers_sign_in_again_in_the_pane()
    {
        var (_, model, _) = Create(With(null, Account("a", "microsoft-brokered", "me@example.com", "authExpired")));

        var row = model.ConnectedAccounts.Single();
        row.CanSignInAgain.Should().BeTrue();
        row.StatusText.Should().Be("Sign in again");
    }

    [Fact]
    public void The_withdraw_consent_text_shows_only_while_a_brokered_account_is_connected()
    {
        var (engine, model, _) = Create();
        model.ShowWithdrawConsent.Should().BeFalse();

        engine.Push(With(null, Account("b", "microsoft", "other@example.com")));
        model.ShowWithdrawConsent.Should().BeFalse("a browser account is not covered by the Windows consent");

        engine.Push(With(null, Account("a", "microsoft-brokered", "me@example.com")));
        model.ShowWithdrawConsent.Should().BeTrue();
        model.WithdrawConsentText.Should().Be(
            "To stop TimeTug reading a Microsoft account's calendars, remove its access at account.live.com/consent/Manage (personal accounts) or on your organization's My Apps page (work accounts).");

        engine.Push(With());
        model.ShowWithdrawConsent.Should().BeFalse();
    }

    [Fact]
    public async Task Removing_a_brokered_account_sends_remove_only_and_asks_nothing_of_the_broker()
    {
        var (engine, model, intent) = Create(With(null, Account("a", "microsoft-brokered", "me@example.com")));

        model.BeginRemove("a");
        await model.ConfirmRemoveAsync();

        engine.Sent.Should().Equal("remove:a");
        intent.Armed.Should().BeEmpty();
    }
}
