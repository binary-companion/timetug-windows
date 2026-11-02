using System.Collections.Specialized;
using System.ComponentModel;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Welcome;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Accounts;

/// <summary>Selection, "-" and Delete, the empty line and the busy rules of the accounts list (L1 to L3, L5, L7 to L10).</summary>
public class AccountsSelectionTests
{
    private static readonly AccountInfo A = Account("a", "google", "a@example.com");
    private static readonly AccountInfo B = Account("b", "icloud", "b@example.com");
    private static readonly AccountInfo C = Account("c", "caldav", "c@example.com");

    private static ListRig Create(params AccountInfo[] accounts)
    {
        var rig = ListRig.Create(None);
        if (accounts.Length > 0)
        {
            rig.Push(accounts);
        }

        return rig;
    }

    private static List<FocusTarget> Focus(AccountsViewModel model)
    {
        var requests = new List<FocusTarget>();
        model.FocusRequested += (_, target) => requests.Add(target);
        return requests;
    }

    private static async Task Remove(ListRig rig, string id)
    {
        rig.Model.SelectedAccountId = id;
        rig.Model.RemoveSelectedCommand.Execute(null);
        await rig.Model.ConfirmRemoveAsync();
    }

    // ---- selection ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Nothing_is_selected_at_first()
    {
        var rig = Create(A, B);

        rig.Model.SelectedAccountId.Should().BeNull();
        rig.Model.ListAccounts.Select(a => a.ConnectionId).Should().Equal("a", "b");
    }

    [Fact]
    public void Remove_is_disabled_with_nothing_selected_and_names_no_account()
    {
        var rig = Create(A);

        rig.Model.CanRemoveSelected.Should().BeFalse();
        rig.Model.RemoveSelectedCommand.CanExecute(null).Should().BeFalse();
        rig.Model.RemoveSelectedAutomationName.Should().Be("Remove account");
    }

    [Fact]
    public void Remove_names_the_selected_row()
    {
        var rig = Create(A, B);

        rig.Model.SelectedAccountId = "b";

        rig.Model.CanRemoveSelected.Should().BeTrue();
        rig.Model.RemoveSelectedCommand.CanExecute(null).Should().BeTrue();
        rig.Model.RemoveSelectedAutomationName.Should().Be("Remove b@example.com");
    }

    [Fact]
    public void Selecting_an_unknown_row_is_ignored()
    {
        var rig = Create(A);

        rig.Model.SelectedAccountId = "nope";

        rig.Model.SelectedAccountId.Should().BeNull();
    }

    [Fact]
    public async Task Remove_is_disabled_while_a_sign_in_runs()
    {
        var rig = Create(A, B);
        rig.Model.SelectedAccountId = "a";
        var changes = new List<string?>();
        ((INotifyPropertyChanged)rig.Model).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        await rig.Model.ConnectAsync("google");
        rig.PushActivity(Working, A, B);

        rig.Model.CanRemoveSelected.Should().BeFalse();
        rig.Model.RemoveSelectedCommand.CanExecute(null).Should().BeFalse();
        changes.Should().Contain(nameof(AccountsViewModel.CanRemoveSelected));
        rig.Model.RemoveSelectedCommand.Execute(null);
        rig.Model.PendingRemoval.Should().BeNull();
    }

    [Fact]
    public void Delete_with_nothing_selected_does_nothing()
    {
        var rig = Create(A);

        rig.Model.RemoveSelectedCommand.Execute(null);

        rig.Model.PendingRemoval.Should().BeNull();
        rig.Engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public void The_question_has_todays_text_and_the_withdrawal_text_only_for_an_on_this_pc_row()
    {
        var rig = Create(A, Account("p", "microsoft-brokered", "work@contoso.example"));

        rig.Model.SelectedAccountId = "a";
        rig.Model.RemoveSelectedCommand.Execute(null);
        rig.Model.RemovalTitle.Should().Be("Remove a@example.com?");
        rig.Model.RemovalBody.Should().Be("Its calendars will no longer appear and their Tug and visibility choices are forgotten.");
        rig.Model.RemovalWithdrawText.Should().BeNull();
        rig.Model.CancelRemove();

        rig.Model.SelectedAccountId = "p";
        rig.Model.RemoveSelectedCommand.Execute(null);
        rig.Model.RemovalTitle.Should().Be("Remove work@contoso.example?");
        rig.Model.RemovalBody.Should().Be("Its calendars will no longer appear and their Tug and visibility choices are forgotten.");
        rig.Model.RemovalWithdrawText.Should().Be(rig.Model.WithdrawConsentText);
    }

    // ---- removal --------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_row_leaves_only_when_the_engines_state_says_so()
    {
        var rig = Create(A, B);

        await Remove(rig, "a");

        rig.Engine.Sent.Should().Equal("remove:a");
        rig.Model.ListAccounts.Select(a => a.ConnectionId).Should().Equal("a", "b");
        rig.Model.SelectedAccountId.Should().Be("a");

        rig.Push(B);
        rig.Model.ListAccounts.Select(a => a.ConnectionId).Should().Equal("b");
    }

    [Fact]
    public async Task After_removal_the_selection_moves_to_the_row_now_in_its_place()
    {
        var rig = Create(A, B, C);
        var focus = Focus(rig.Model);

        await Remove(rig, "b");
        rig.Push(A, C);

        rig.Model.SelectedAccountId.Should().Be("c");
        focus.Should().Equal(new FocusTarget(FocusKind.Row, "c"));
    }

    [Fact]
    public async Task After_removing_the_last_row_the_selection_moves_to_the_previous_one()
    {
        var rig = Create(A, B, C);
        var focus = Focus(rig.Model);

        await Remove(rig, "c");
        rig.Push(A, B);

        rig.Model.SelectedAccountId.Should().Be("b");
        focus.Should().Equal(new FocusTarget(FocusKind.Row, "b"));
    }

    [Fact]
    public async Task After_removing_the_only_row_focus_goes_to_the_add_button()
    {
        var rig = Create(A);
        var focus = Focus(rig.Model);

        await Remove(rig, "a");
        rig.Push();

        rig.Model.SelectedAccountId.Should().BeNull();
        focus.Should().Equal(new FocusTarget(FocusKind.AddButton, null));
    }

    [Fact]
    public void A_vanished_selection_is_cleared_without_a_focus_request()
    {
        var rig = Create(A, B);
        var focus = Focus(rig.Model);
        rig.Model.SelectedAccountId = "a";

        rig.Push(B);

        rig.Model.SelectedAccountId.Should().BeNull();
        focus.Should().BeEmpty();
    }

    [Fact]
    public async Task The_engine_publishing_the_removal_while_a_sign_in_runs_still_moves_the_selection()
    {
        var rig = Create(A, B);
        var focus = Focus(rig.Model);
        await Remove(rig, "a");

        rig.PushActivity(Working, B);

        rig.Model.SelectedAccountId.Should().Be("b");
        focus.Should().Equal(new FocusTarget(FocusKind.Row, "b"));
    }

    [Fact]
    public async Task An_engine_refusal_of_the_removal_keeps_the_row_and_shows_the_message()
    {
        var rig = Create(A, B);
        var focus = Focus(rig.Model);
        await Remove(rig, "a");

        rig.PushActivity(new AccountActivity(false, "Could not remove the account.", "browser"), A, B);

        rig.Model.ListAccounts.Select(a => a.ConnectionId).Should().Equal("a", "b");
        rig.Model.ErrorText.Should().Be("Could not remove the account.");
        rig.Model.SelectedAccountId.Should().Be("a");
        focus.Should().BeEmpty();

        // The row going away later for another reason is not mistaken for the refused removal.
        rig.Push(B);
        focus.Should().BeEmpty();
        rig.Model.SelectedAccountId.Should().BeNull();
    }

    // ---- stable updates -------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Engine_publications_keep_the_row_instances_and_the_selection()
    {
        var rig = Create(A, B);
        rig.Model.SelectedAccountId = "b";
        var focus = Focus(rig.Model);
        var rows = rig.Model.ListAccounts.ToList();
        var collectionEvents = 0;
        ((INotifyCollectionChanged)rig.Model.ListAccounts).CollectionChanged += (_, _) => collectionEvents++;
        var selectionEvents = 0;
        ((INotifyPropertyChanged)rig.Model).PropertyChanged += (_, e) => selectionEvents += e.PropertyName == nameof(AccountsViewModel.SelectedAccountId) ? 1 : 0;

        for (var i = 0; i < 10; i++)
        {
            rig.Push(A, B);
        }

        collectionEvents.Should().Be(0);
        selectionEvents.Should().Be(0);
        rig.Model.SelectedAccountId.Should().Be("b");
        rig.Model.ListAccounts.Should().Equal(rows);
        focus.Should().BeEmpty();
    }

    [Fact]
    public void A_changed_row_is_replaced_in_place_and_only_that_row()
    {
        var rig = Create(A, B, C);
        rig.Model.SelectedAccountId = "b";
        var first = rig.Model.ListAccounts[0];
        var third = rig.Model.ListAccounts[2];
        var events = new List<NotifyCollectionChangedEventArgs>();
        ((INotifyCollectionChanged)rig.Model.ListAccounts).CollectionChanged += (_, e) => events.Add(e);

        rig.Push(A, Account("b", "icloud", "b@example.com", "authExpired"), C);

        events.Should().ContainSingle().Which.Action.Should().Be(NotifyCollectionChangedAction.Replace);
        rig.Model.ListAccounts[0].Should().BeSameAs(first);
        rig.Model.ListAccounts[2].Should().BeSameAs(third);
        rig.Model.ListAccounts[1].CanSignInAgain.Should().BeTrue();
        rig.Model.SelectedAccountId.Should().Be("b");
    }

    [Fact]
    public void A_new_account_is_appended_without_disturbing_the_others()
    {
        var rig = Create(A);
        var events = new List<NotifyCollectionChangedEventArgs>();
        ((INotifyCollectionChanged)rig.Model.ListAccounts).CollectionChanged += (_, e) => events.Add(e);

        rig.Push(A, B);

        events.Should().ContainSingle().Which.Action.Should().Be(NotifyCollectionChangedAction.Add);
        rig.Model.ListAccounts.Select(a => a.ConnectionId).Should().Equal("a", "b");
    }

    // ---- the empty line and failed accounts -----------------------------------------------------------------------------------------

    [Fact]
    public void The_empty_line_shows_only_with_no_list_rows_and_the_pinned_row_stays()
    {
        var rig = ListRig.Create(Named);
        rig.Model.ShowListEmptyText.Should().BeTrue();
        rig.Model.ListEmptyText.Should().Be("No accounts yet. Choose + to add one.");
        rig.Model.DeviceRow.IsVisible.Should().BeTrue();

        rig.Push(A);
        rig.Model.ShowListEmptyText.Should().BeFalse();

        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));
        rig.Model.ShowListEmptyText.Should().BeTrue("the pinned account is not a list row");
        rig.Model.DeviceRow.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void The_empty_line_waits_for_the_first_state()
    {
        var engine = new FakeEngineClient();
        var model = new AccountsViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer());

        model.ShowListEmptyText.Should().BeFalse();
    }

    [Fact]
    public void An_error_line_is_not_a_row_and_a_failed_account_is_a_row_with_sign_in_again()
    {
        var rig = Create();
        rig.PushActivity(new AccountActivity(false, "Something the engine said.", "browser"));
        rig.Model.ListAccounts.Should().BeEmpty();
        rig.Model.ErrorText.Should().Be("Something the engine said.");

        rig.Push(Account("x", "caldav", "x@example.com", "authExpired"), Account("y", "google", "y@example.com", "failing") with { BuildFailure = true });

        rig.Model.ListAccounts.Select(a => a.CanSignInAgain).Should().Equal(true, true);
        rig.Model.ListAccounts.Select(a => a.StatusText).Should().Equal("Sign in again", "Can’t start this account");
    }

    [Fact]
    public async Task A_late_listing_shows_the_pinned_row_without_raising_a_focus_request()
    {
        var rig = ListRig.Create(pending: false, gated: true);
        rig.Push(A);
        rig.Model.SelectedAccountId = "a";
        var focus = Focus(rig.Model);

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => rig.Model.DeviceRow.IsVisible);

        rig.Model.DeviceRow.IsVisible.Should().BeTrue();
        rig.Model.SelectedAccountId.Should().Be("a");
        focus.Should().BeEmpty();
    }

    [Fact]
    public void Connected_accounts_carry_their_email_for_matching()
    {
        var rig = Create(A, Account("n", "caldav", "Team"));

        rig.Model.ConnectedAccounts.Select(a => a.Email).Should().Equal("a@example.com", null);
    }
}
