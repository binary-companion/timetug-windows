using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>"Microsoft account on this PC" in the providers list and the Accounts pane: ordering, names, intents, waiting, failure, tag and the withdraw-consent text.</summary>
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

        public ValueTask<string?> GetEmailAsync(CancellationToken cancellationToken)
        {
            Asked++;
            return ValueTask.FromResult(email);
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

    private static Task Press(System.Windows.Input.ICommand? command) => ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)command!).ExecuteAsync(null);

    private static AccountInfo Account(string id, string kind, string name, string status = "ok") => new(id, kind, name, status, 2);

    private static AppState With(AccountActivity? activity = null, params AccountInfo[] accounts) => States.With(accounts, activity: activity, kinds: KindsWithBrokered);

    private static AppState Idle(string? error = null) => States.Empty(KindsWithBrokered, new AccountActivity(false, error, "browser"));

    // ---- the list -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_brokered_provider_is_ordered_first_and_named_on_this_pc()
    {
        var (_, model, _) = Create();

        model.Providers[0].Id.Should().Be("microsoft-brokered");
        model.Providers[0].Name.Should().Be("Microsoft account on this PC");
        model.Providers.Select(p => p.Id).Should().Equal("microsoft-brokered", "microsoft-brokered:pick", "google", "microsoft", "icloud", "caldav");
    }

    [Fact]
    public void Another_Microsoft_account_follows_it_and_the_browser_Microsoft_is_relabelled()
    {
        var (_, model, _) = Create();

        model.Providers.Select(p => p.Name).Should().Equal(
            "Microsoft account on this PC", "Another Microsoft account", "Google", "Microsoft (browser)", "iCloud", "Other (CalDAV)");
    }

    [Fact]
    public void Browser_Microsoft_is_relabelled_only_when_the_brokered_kind_is_listed()
    {
        var (_, model, _) = Create(States.Empty());

        model.Providers.Select(p => p.Name).Should().Equal("Google", "Microsoft", "iCloud", "Other (CalDAV)");
    }

    [Fact]
    public void Without_the_brokered_kind_the_list_is_unchanged()
    {
        var (_, model, _) = Create(States.Empty());

        model.Providers.Select(p => p.Id).Should().Equal("google", "microsoft", "icloud", "caldav");
        model.Providers.Should().OnlyContain(p => p.Intent == null && !p.OffersBrowserFallback);
    }

    [Fact]
    public void An_unconfigured_brokered_kind_shows_disabled_and_adds_no_picker_item()
    {
        var kinds = KindsWithBrokered.Select(k => k.Id == "microsoft-brokered" ? k with { IsConfigured = false } : k).ToList();
        var (engine, model, _) = Create(States.Empty(kinds));

        model.Providers.Select(p => p.Id).Should().Equal("microsoft-brokered", "google", "microsoft", "icloud", "caldav");
        model.Providers[0].IsEnabled.Should().BeFalse();
        engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public void The_pre_click_text_shows_under_the_brokered_card_and_under_the_picker_card()
    {
        var (_, model, _) = Create();

        const string text = "Windows will ask you to let TimeTug read your calendars. TimeTug stores no password and no sign-in token.";
        model.Providers[0].Hint.Should().Be(text);
        model.Providers[1].Hint.Should().Be(text);
        model.Providers[3].Hint.Should().Be("Opens your browser to sign in");
    }

    [Fact]
    public void Every_new_card_has_words_for_a_screen_reader()
    {
        var (_, model, _) = Create();

        model.Providers[0].AutomationName.Should().Be("Connect Microsoft account on this PC");
        model.Providers[1].AutomationName.Should().Be("Connect Another Microsoft account");
    }

    // ---- intents ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_windows_account_item_arms_the_windows_account_intent_and_adds_the_brokered_kind()
    {
        var (engine, model, intent) = Create();

        await Press(model.Providers[0].ConnectCommand);

        intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0));
        engine.Sent.Should().Equal("add:microsoft-brokered");
    }

    [Fact]
    public async Task The_another_account_item_arms_the_picker_intent_before_the_engine_is_asked()
    {
        var (engine, model, intent) = Create();

        await Press(model.Providers[1].ConnectCommand);

        intent.Armed.Should().Equal((BrokerIntent.PickAccount, 0)); // armed before the add command is sent
        engine.Sent.Should().Equal("add:microsoft-brokered");
        model.Providers[1].Intent.Should().Be(BrokerIntent.PickAccount);
    }

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

        model.Providers[0].Name.Should().Be("Microsoft account on this PC");
    }

    private sealed class CancelledAccounts : IWindowsAccountSource
    {
        public ValueTask<string?> GetEmailAsync(CancellationToken cancellationToken) => throw new OperationCanceledException();
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

    // ---- declined, unavailable, blocked -------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_declined_attempt_shows_no_error_and_returns_to_the_list()
    {
        var (engine, model, _) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));

        // The person closed the Windows dialog: the engine stops working and reports nothing.
        engine.Push(Idle());

        model.IsBusy.Should().BeFalse();
        model.ErrorText.Should().BeNull();
        model.Providers.Should().OnlyContain(p => !p.HasError && !p.OffersBrowserFallback && p.IsEnabled);
    }

    [Fact]
    public async Task An_unavailable_attempt_shows_windows_sign_in_isnt_available_and_the_browser_button()
    {
        var (engine, model, _) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));

        engine.Push(Idle("Sign-in failed: Windows sign-in is not available."));

        var card = model.Providers[0];
        card.Error.Should().Be("Windows sign-in isn't available here");
        card.OffersBrowserFallback.Should().BeTrue();
        card.BrowserFallbackText.Should().Be("Use your browser instead");
        card.BrowserFallbackAutomationName.Should().Be("Use your browser to sign in to Microsoft instead");
        model.Providers[1].OffersBrowserFallback.Should().BeFalse("only the card the person pressed explains");
        model.ErrorText.Should().BeNull("the card already shows it");
    }

    [Fact]
    public async Task The_browser_button_connects_the_browser_kind()
    {
        var (engine, model, intent) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));
        engine.Push(Idle("Sign-in failed: Windows sign-in is not available."));
        engine.Sent.Clear();
        intent.Armed.Clear();

        await Press(model.Providers[0].BrowserFallbackCommand);

        engine.Sent.Should().Equal("add:microsoft");
        intent.Armed.Should().BeEmpty("the browser path needs no broker intent");
        model.Providers.Single(p => p.Id == "microsoft").Error.Should().BeNull();
        model.Providers[0].OffersBrowserFallback.Should().BeFalse("starting another attempt clears the explanation");
    }

    [Fact]
    public async Task An_admin_approval_failure_shows_the_engines_text_and_keeps_the_browser_button()
    {
        var (engine, model, _) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.PickAccount);
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));

        engine.Push(Idle("Sign-in failed: admin approval is required."));

        var card = model.Providers[1];
        card.Error.Should().Contain("Microsoft");
        card.ErrorDetail.Should().Be("Sign-in failed: admin approval is required.");
        card.OffersBrowserFallback.Should().BeTrue();
        card.Error.Should().NotBe("Windows sign-in isn't available here");
    }

    [Fact]
    public async Task Any_other_brokered_failure_shows_the_usual_error_and_still_offers_the_browser()
    {
        var (engine, model, _) = Create();
        await model.ConnectBrokeredAsync(BrokerIntent.WindowsAccount);
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));

        engine.Push(Idle("Sign-in failed (network)."));

        model.Providers[0].Error.Should().Contain("Couldn’t connect");
        model.Providers[0].ErrorDetail.Should().Be("Sign-in failed (network).");
        model.Providers[0].OffersBrowserFallback.Should().BeTrue();
    }

    [Fact]
    public async Task A_failed_browser_attempt_never_offers_the_browser_button()
    {
        var (engine, model, _) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.Empty(KindsWithBrokered, new AccountActivity(true, null, "browser")));
        engine.Push(Idle("Sign-in failed: access denied."));

        model.Providers.Should().OnlyContain(p => !p.OffersBrowserFallback);
    }

    // ---- naming the Windows account -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_card_names_the_windows_account_when_the_broker_lists_one()
    {
        var (_, model, _) = Create();
        var accounts = new FakeWindowsAccounts("me@example.com");
        model.WindowsAccounts = accounts;

        await model.RefreshWindowsAccountAsync();

        model.Providers[0].Name.Should().Be("Use me@example.com");
        model.Providers[0].AutomationName.Should().Be("Connect Use me@example.com");
        model.Providers[1].Name.Should().Be("Another Microsoft account");
    }

    [Fact]
    public async Task The_card_falls_back_to_the_unlabelled_text_when_no_account_is_named()
    {
        var (_, model, _) = Create();
        model.WindowsAccounts = new FakeWindowsAccounts(null);

        await model.RefreshWindowsAccountAsync();

        model.Providers[0].Name.Should().Be("Microsoft account on this PC");
    }

    [Fact]
    public async Task A_broker_that_throws_leaves_the_unlabelled_text()
    {
        var (_, model, _) = Create();
        model.WindowsAccounts = new ThrowingAccounts();

        await model.RefreshWindowsAccountAsync();

        model.Providers[0].Name.Should().Be("Microsoft account on this PC");
    }

    [Fact]
    public async Task The_name_survives_the_engine_republishing_its_state()
    {
        var (engine, model, _) = Create();
        model.WindowsAccounts = new FakeWindowsAccounts("me@example.com");
        await model.RefreshWindowsAccountAsync();

        engine.Push(States.Empty(KindsWithBrokered));
        engine.Push(States.Empty(KindsWithBrokered.Where(k => k.Id != "icloud").ToList()));

        model.Providers[0].Name.Should().Be("Use me@example.com");
    }

    private sealed class ThrowingAccounts : IWindowsAccountSource
    {
        public ValueTask<string?> GetEmailAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("broker gone");
    }
}
