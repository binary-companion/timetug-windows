using System.Collections.Specialized;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Accounts;
using TimeTug.Presentation.Tests.Settings;
using TimeTug.Presentation.Welcome;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Welcome;

/// <summary>
/// The Windows Microsoft account is a row in the accounts box on the first-run window (spec amendment A2): it is there before Continue ("Ready to connect"), becomes
/// "Connecting…" while Windows asks, stays with a clear status when the dialog is declined, closed or fails, and is removed with "-" (which records the dismissal).
/// Nothing is requested from Windows until Continue.
/// </summary>
public class WelcomeWindowsRowTests
{
    private const string Ready = "Windows will ask you to allow TimeTug when you continue.";

    private sealed record Rig(FakeEngineClient Engine, WelcomeViewModel Model, AccountsBrokeredTests.FakeIntent Intent, FakeListing Source, FakeDismissals Dismissals);

    private static Rig Create(WindowsAccountListing? listing = null, bool gated = false, bool dismissed = false, bool withDismissals = true)
    {
        var engine = new FakeEngineClient();
        var model = new WelcomeViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), new FakeTimeProvider(States.Noon));
        var intent = new AccountsBrokeredTests.FakeIntent(engine.Sent);
        model.Accounts.BrokerIntents = intent;
        var dismissals = new FakeDismissals(dismissed);
        if (withDismissals)
        {
            model.Accounts.Dismissals = dismissals;
        }

        var source = new FakeListing(listing ?? Named);
        if (gated)
        {
            source.Gate = new TaskCompletionSource<WindowsAccountListing>();
        }

        engine.Push(ListRig.Empty());
        model.Accounts.WindowsAccounts = source;
        model.GetStartedCommand.Execute(null);
        return new Rig(engine, model, intent, source, dismissals);
    }

    private static ConnectedAccount OnlyRow(Rig rig) => rig.Model.Accounts.ListAccounts.Single();

    private static async Task Decline(Rig rig)
    {
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(ListRig.Empty(Idle));
    }

    // ---- the row before Continue ----------------------------------------------------------------------------------------------------

    [Fact]
    public void The_windows_account_is_a_row_in_the_box_before_Continue_with_the_microsoft_logo_its_email_and_a_ready_status()
    {
        var rig = Create();

        var row = OnlyRow(rig);
        row.IsPending.Should().BeTrue();
        row.ConnectionId.Should().Be(AccountsViewModel.WindowsOfferRowId);
        row.KindName.Should().Be("Microsoft");
        row.Mark.Should().Be(ProviderMark.Microsoft);
        row.LogoAutomationName.Should().Be("Microsoft logo");
        row.Name.Should().Be("me@example.com");
        row.StatusText.Should().Be("Ready to connect");
        row.HintText.Should().Be(Ready);
        row.HasHint.Should().BeTrue();
        row.NeedsAttention.Should().BeFalse();
        row.CanSignInAgain.Should().BeFalse("it is not an account yet; there is nothing to sign in again");
        row.AutomationName.Should().Be("Microsoft, me@example.com, Ready to connect. " + Ready);
        rig.Model.Accounts.ShowListEmptyText.Should().BeFalse("the box is not empty");
        rig.Model.Accounts.ConnectedAccounts.Should().BeEmpty("nothing is connected until Continue");
    }

    [Fact]
    public void Nothing_but_the_listing_has_gone_to_windows_while_the_row_is_shown()
    {
        var rig = Create();

        rig.Source.Calls.Should().Equal("listing");
        rig.Intent.Armed.Should().BeEmpty();
        rig.Engine.Sent.Should().BeEmpty();
        rig.Model.Accounts.IsBusy.Should().BeFalse();
    }

    [Fact]
    public void A_listing_that_names_nothing_in_particular_gets_a_row_without_an_email()
    {
        var rig = Create(Unnamed);

        var row = OnlyRow(rig);
        row.IsPending.Should().BeTrue();
        row.Name.Should().Contain("Windows").And.NotContain("@");
        row.StatusText.Should().Be("Ready to connect");
        row.Email.Should().BeNull();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("unavailable")]
    [InlineData("dismissed")]
    public void No_row_and_the_empty_text_when_nothing_is_offered(string why)
    {
        var rig = why switch
        {
            "none" => Create(None),
            "unavailable" => Create(Unavailable),
            _ => Create(dismissed: true),
        };

        rig.Model.Accounts.ListAccounts.Should().BeEmpty();
        rig.Model.Accounts.ShowListEmptyText.Should().BeTrue();
        rig.Model.CanContinue.Should().BeFalse();
    }

    [Fact]
    public async Task A_late_listing_adds_the_row_in_place_and_tells_the_list()
    {
        var rig = Create(gated: true);
        rig.Model.Accounts.ListAccounts.Should().BeEmpty("the listing has not answered");
        var events = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)rig.Model.Accounts.ListAccounts).CollectionChanged += (_, e) => events.Add(e.Action);
        var changed = new List<string?>();
        rig.Model.Accounts.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => rig.Model.Accounts.ListAccounts.Count == 1);

        OnlyRow(rig).IsPending.Should().BeTrue();
        events.Should().ContainSingle().Which.Should().Be(NotifyCollectionChangedAction.Add);
        changed.Should().Contain(nameof(AccountsViewModel.ShowListEmptyText));
        rig.Model.Accounts.ShowListEmptyText.Should().BeFalse();
        rig.Model.Step.Should().Be(WelcomeStep.Connect);
    }

    [Fact]
    public void The_row_is_selectable_and_minus_is_named_after_it()
    {
        var rig = Create();

        rig.Model.Accounts.SelectedAccountId = AccountsViewModel.WindowsOfferRowId;

        rig.Model.Accounts.SelectedAccountId.Should().Be(AccountsViewModel.WindowsOfferRowId);
        rig.Model.Accounts.CanRemoveSelected.Should().BeTrue();
        rig.Model.Accounts.RemoveSelectedAutomationName.Should().Be("Remove me@example.com");
    }

    [Fact]
    public void The_settings_pane_never_shows_the_row_although_it_knows_the_listing()
    {
        var rig = ListRig.Create(Named);

        rig.Push();

        rig.Model.WindowsAccountOffered.Should().BeTrue("the listing is known, so a removal there is remembered");
        rig.Model.ListAccounts.Should().BeEmpty();
        rig.Model.ShowListEmptyText.Should().BeTrue();
        rig.Model.BeginRemove(AccountsViewModel.WindowsOfferRowId);
        rig.Dismissals.DismissCalls.Should().Be(0, "there is no row to remove");
    }

    // ---- Continue: connecting, then connected ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Continue_makes_the_row_connecting_with_no_hint_and_the_connected_account_takes_its_place()
    {
        var rig = Create();

        await Press(rig.Model.ContinueCommand);

        rig.Intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0));
        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
        var connecting = OnlyRow(rig);
        connecting.IsPending.Should().BeTrue();
        connecting.StatusText.Should().Be("Connecting…");
        connecting.HasHint.Should().BeFalse();
        connecting.NeedsAttention.Should().BeFalse();
        connecting.AutomationName.Should().Be("Microsoft, me@example.com, Connecting…");
        rig.Model.Accounts.CanRemoveSelected.Should().BeFalse("minus is off while Windows is being asked");

        rig.Engine.Push(ListRig.Empty(Working));
        OnlyRow(rig).StatusText.Should().Be("Connecting…");
        rig.Engine.Push(With(Idle, Account("p", "microsoft-brokered", "me@example.com")));

        var connected = OnlyRow(rig);
        connected.IsPending.Should().BeFalse();
        connected.ConnectionId.Should().Be("p");
        connected.StatusText.Should().Be("Connected");
        connected.Mark.Should().Be(ProviderMark.Microsoft);
        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Model.Accounts.WindowsAccountOffered.Should().BeFalse();
    }

    [Fact]
    public async Task While_another_sign_in_runs_the_row_stays_ready()
    {
        var rig = Create();

        await Press(rig.Model.Accounts.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);

        OnlyRow(rig).StatusText.Should().Be("Ready to connect");
    }

    // ---- declined, closed, failed or cancelled --------------------------------------------------------------------------------------

    [Fact]
    public async Task Declining_keeps_the_row_with_a_clear_status_and_no_error_words()
    {
        var rig = Create();

        await Decline(rig);

        var row = OnlyRow(rig);
        row.IsPending.Should().BeTrue();
        row.StatusText.Should().Be("Not connected. Choose Continue to try again.");
        row.NeedsAttention.Should().BeTrue("the status is in the warning colour with a mark, never colour alone");
        row.HasHint.Should().BeFalse();
        row.AutomationName.Should().Be("Microsoft, me@example.com, Not connected. Choose Continue to try again.");
        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Model.Accounts.ErrorText.Should().BeNull();
        rig.Model.Accounts.SignInFailureText.Should().BeNull();
        rig.Model.Accounts.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.CanContinue.Should().BeTrue();
        rig.Dismissals.DismissCalls.Should().Be(0, "a decline is not remembered");
    }

    [Fact]
    public async Task A_failure_keeps_the_row_with_the_same_status_and_the_usual_failure_text()
    {
        var rig = Create();
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(ListRig.Empty(new AccountActivity(false, "Sign-in failed (network).", "browser")));

        OnlyRow(rig).StatusText.Should().Be("Not connected. Choose Continue to try again.");
        rig.Model.Accounts.SignInFailureDetail.Should().Be("Sign-in failed (network).");
        rig.Model.Accounts.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.Step.Should().Be(WelcomeStep.Connect);
    }

    [Fact]
    public async Task Cancelling_the_wait_counts_as_not_connected()
    {
        var rig = Create();
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        await rig.Model.Accounts.CancelAsync();
        rig.Engine.Push(ListRig.Empty(Idle));

        OnlyRow(rig).StatusText.Should().Be("Not connected. Choose Continue to try again.");
        rig.Model.Step.Should().Be(WelcomeStep.Connect);
    }

    [Fact]
    public async Task Continue_again_after_a_decline_asks_windows_again_and_the_row_is_connecting_once_more()
    {
        var rig = Create();
        await Decline(rig);
        rig.Engine.Sent.Clear();

        await Press(rig.Model.ContinueCommand);

        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
        OnlyRow(rig).StatusText.Should().Be("Connecting…");
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("p", "microsoft-brokered", "me@example.com")));
        rig.Model.Step.Should().Be(WelcomeStep.Connected);
    }

    [Fact]
    public async Task After_a_decline_with_another_account_connected_the_row_says_continue_goes_on_without_it_and_Continue_does()
    {
        var rig = Create();
        await Decline(rig);
        await Press(rig.Model.Accounts.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));
        rig.Engine.Sent.Clear();
        rig.Intent.Armed.Clear();

        var rows = rig.Model.Accounts.ListAccounts;
        rows.Select(r => r.ConnectionId).Should().Equal(AccountsViewModel.WindowsOfferRowId, "g");
        rows[0].StatusText.Should().Be("Not connected. Continue goes on without it.");
        rows[0].NeedsAttention.Should().BeTrue();

        await Press(rig.Model.ContinueCommand);

        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
        rig.Intent.Armed.Should().BeEmpty();
    }

    // ---- other accounts alongside ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task An_account_added_with_plus_joins_below_the_row_which_stays_first_and_Continue_still_asks_windows()
    {
        var rig = Create();
        await Press(rig.Model.Accounts.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));
        rig.Engine.Sent.Clear();

        rig.Model.Accounts.ListAccounts.Select(r => r.ConnectionId).Should().Equal(AccountsViewModel.WindowsOfferRowId, "g");
        rig.Model.Accounts.ListAccounts[0].StatusText.Should().Be("Ready to connect");
        rig.Model.Step.Should().Be(WelcomeStep.Connect);

        await Press(rig.Model.ContinueCommand);

        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
    }

    [Fact]
    public async Task The_microsoft_picker_with_another_email_adds_a_second_row_and_the_offer_stays()
    {
        var rig = Create();
        await rig.Model.ConnectAsync("microsoft-brokered:pick");
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(With(Idle, Account("w", "microsoft-brokered", "work@contoso.example")));

        rig.Model.Accounts.ListAccounts.Select(r => r.ConnectionId).Should().Equal(AccountsViewModel.WindowsOfferRowId, "w");
        rig.Model.Accounts.ListAccounts[0].StatusText.Should().Be("Ready to connect", "a picker attempt is not the Windows account's");
    }

    [Fact]
    public async Task The_microsoft_picker_choosing_the_windows_account_replaces_the_row_by_the_connected_one()
    {
        var rig = Create();
        await rig.Model.ConnectAsync("microsoft-brokered:pick");
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(With(Idle, Account("p", "microsoft-brokered", "Me@Example.com")));

        rig.Model.Accounts.ListAccounts.Select(r => r.ConnectionId).Should().Equal("p");
        rig.Model.Step.Should().Be(WelcomeStep.Connect, "an account added with + never moves the window on");
    }

    [Fact]
    public void A_browser_account_with_the_windows_email_leaves_no_row_because_the_engine_would_refuse_it()
    {
        var rig = Create();

        rig.Engine.Push(With(Idle, Account("b", "microsoft", "me@example.com")));

        rig.Model.Accounts.ListAccounts.Select(r => r.ConnectionId).Should().Equal("b");
    }

    // ---- removing the row with minus -------------------------------------------------------------------------------------------------

    [Fact]
    public void Minus_drops_the_row_at_once_without_a_question_and_records_the_dismissal()
    {
        var rig = Create();
        rig.Model.Accounts.SelectedAccountId = AccountsViewModel.WindowsOfferRowId;
        var focus = new List<FocusTarget>();
        rig.Model.Accounts.FocusRequested += (_, t) => focus.Add(t);

        rig.Model.Accounts.RemoveSelectedCommand.Execute(null);

        rig.Model.Accounts.PendingRemoval.Should().BeNull("nothing was connected, so there is nothing to confirm");
        rig.Model.Accounts.ListAccounts.Should().BeEmpty();
        rig.Model.Accounts.ShowListEmptyText.Should().BeTrue();
        rig.Model.Accounts.WindowsAccountOffered.Should().BeFalse();
        rig.Model.Accounts.SelectedAccountId.Should().BeNull();
        rig.Model.CanContinue.Should().BeFalse("an empty box has nothing to continue to");
        rig.Dismissals.DismissCalls.Should().Be(1);
        rig.Dismissals.IsDismissed.Should().BeTrue();
        focus.Should().Equal(new FocusTarget(FocusKind.AddButton, null));
        rig.Engine.Sent.Should().BeEmpty("nothing was asked of the engine");
        rig.Intent.Armed.Should().BeEmpty("and nothing of Windows");
        rig.Source.Calls.Should().Equal("listing");
    }

    [Fact]
    public async Task Minus_with_another_account_in_the_list_moves_selection_and_focus_to_it_and_Continue_goes_on_without_asking()
    {
        var rig = Create();
        await Press(rig.Model.Accounts.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));
        var focus = new List<FocusTarget>();
        rig.Model.Accounts.FocusRequested += (_, t) => focus.Add(t);
        rig.Model.Accounts.SelectedAccountId = AccountsViewModel.WindowsOfferRowId;
        rig.Engine.Sent.Clear();

        rig.Model.Accounts.RemoveSelectedCommand.Execute(null);

        rig.Model.Accounts.ListAccounts.Select(r => r.ConnectionId).Should().Equal("g");
        rig.Model.Accounts.SelectedAccountId.Should().Be("g");
        focus.Should().Equal(new FocusTarget(FocusKind.Row, "g"));
        rig.Model.CanContinue.Should().BeTrue();

        await Press(rig.Model.ContinueCommand);

        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
        rig.Intent.Armed.Should().BeEmpty();
    }

    [Fact]
    public void Minus_is_off_with_nothing_selected_and_selecting_another_row_leaves_the_offer_alone()
    {
        var rig = Create();
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));

        rig.Model.Accounts.CanRemoveSelected.Should().BeFalse();
        rig.Model.Accounts.RemoveSelectedCommand.Execute(null);
        rig.Model.Accounts.SelectedAccountId = "g";
        rig.Model.Accounts.RemoveSelectedCommand.Execute(null);

        rig.Model.Accounts.PendingRemoval.Should().NotBeNull("a connected account still asks first");
        rig.Dismissals.DismissCalls.Should().Be(0);
    }

    [Fact]
    public async Task The_row_cannot_be_removed_while_windows_is_being_asked()
    {
        var rig = Create();
        rig.Model.Accounts.SelectedAccountId = AccountsViewModel.WindowsOfferRowId;
        await Press(rig.Model.ContinueCommand);

        rig.Model.Accounts.RemoveSelectedCommand.CanExecute(null).Should().BeFalse();
        rig.Model.Accounts.BeginRemove(AccountsViewModel.WindowsOfferRowId);

        rig.Model.Accounts.ListAccounts.Should().ContainSingle().Which.IsPending.Should().BeTrue();
        rig.Dismissals.DismissCalls.Should().Be(0);
    }

    [Fact]
    public void The_row_goes_for_the_session_even_without_a_store_to_remember_it()
    {
        var rig = Create(withDismissals: false);
        rig.Model.Accounts.SelectedAccountId = AccountsViewModel.WindowsOfferRowId;

        rig.Model.Accounts.RemoveSelectedCommand.Execute(null);

        rig.Model.Accounts.ListAccounts.Should().BeEmpty();
        rig.Model.Accounts.WindowsAccountOffered.Should().BeFalse();
    }

    [Fact]
    public async Task A_plus_microsoft_after_the_removal_adds_the_windows_account_back_as_a_connected_row()
    {
        var rig = Create();
        rig.Model.Accounts.SelectedAccountId = AccountsViewModel.WindowsOfferRowId;
        rig.Model.Accounts.RemoveSelectedCommand.Execute(null);

        await rig.Model.Accounts.ConnectAsync("microsoft-brokered:pick");
        rig.Intent.Armed.Should().Equal((BrokerIntent.PickAccount, 0));
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("p", "microsoft-brokered", "me@example.com")));

        rig.Model.Accounts.ListAccounts.Select(r => r.ConnectionId).Should().Equal("p");
        rig.Model.Accounts.WindowsAccountOffered.Should().BeFalse();
    }

    [Fact]
    public async Task Skip_for_now_adds_nothing_and_records_nothing()
    {
        var rig = Create();
        var closed = 0;
        rig.Model.CloseRequested += (_, _) => closed++;

        await Press(rig.Model.SkipCommand);

        closed.Should().Be(1);
        rig.Engine.Sent.Should().BeEmpty();
        rig.Intent.Armed.Should().BeEmpty();
        rig.Dismissals.DismissCalls.Should().Be(0);
        rig.Source.Calls.Should().Equal("listing");
    }
}
