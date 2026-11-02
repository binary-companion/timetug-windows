using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Accounts;

/// <summary>
/// The Windows account is an ordinary row: Windows lists it, the first-run Continue may add it (spec A1.1 to A1.4, D16 to D18), and removing it is remembered.
/// </summary>
public class WindowsAccountOfferTests
{
    // ---- when Continue has a Windows account to add ----------------------------------------------------------------------------------

    [Fact]
    public void A_listing_naming_one_account_is_offered()
    {
        var rig = Create(Named);
        rig.Model.WindowsAccountOffered.Should().BeTrue();
        rig.Model.ListingPending.Should().BeFalse();
        rig.Source.Calls.Should().Equal("listing");
    }

    [Fact]
    public void A_listing_that_names_nothing_in_particular_is_offered_too() => Create(Unnamed).Model.WindowsAccountOffered.Should().BeTrue();

    [Theory]
    [InlineData("none")]
    [InlineData("unavailable")]
    public void A_local_only_account_and_a_failed_listing_offer_nothing(string listing) =>
        Create(listing == "none" ? None : Unavailable).Model.WindowsAccountOffered.Should().BeFalse();

    [Fact]
    public void Without_a_windows_account_source_nothing_is_offered_and_nothing_is_pending()
    {
        var model = Create(withSource: false).Model;
        model.WindowsAccountOffered.Should().BeFalse();
        model.ListingPending.Should().BeFalse();
    }

    [Fact]
    public void An_unconfigured_brokered_kind_offers_nothing_even_when_windows_lists_an_account()
    {
        var kinds = Kinds.Select(k => k.Id == "microsoft-brokered" ? k with { IsConfigured = false } : k).ToList();
        var rig = Create(Named, first: TimeTug.Presentation.Tests.Welcome.States.Empty(kinds));
        rig.Model.WindowsAccountOffered.Should().BeFalse();
        rig.Model.ListingPending.Should().BeFalse();
    }

    [Fact]
    public async Task A_late_listing_is_pending_and_then_offered()
    {
        var rig = Create(gated: true);
        rig.Model.ListingPending.Should().BeTrue();
        rig.Model.WindowsAccountOffered.Should().BeFalse();

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => rig.Model.WindowsAccountOffered);

        rig.Model.ListingPending.Should().BeFalse();
        rig.Model.WindowsAccountOffered.Should().BeTrue();
    }

    [Fact]
    public void A_recorded_dismissal_offers_nothing_and_never_asks_the_broker()
    {
        var rig = Create(Named, dismissed: true);

        rig.Model.WindowsAccountOffered.Should().BeFalse();
        rig.Model.ListingPending.Should().BeFalse();
        rig.Source.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("me@example.com")]
    [InlineData("Me@Example.com")]
    [InlineData("ME@EXAMPLE.COM")]
    public void A_connected_brokered_account_with_the_listed_email_is_not_offered_again(string connectedEmail)
    {
        var rig = Create(Named);

        rig.Push(Account("p", "microsoft-brokered", connectedEmail));

        rig.Model.WindowsAccountOffered.Should().BeFalse();
    }

    [Fact]
    public void A_brokered_account_with_another_email_does_not_stop_the_offer()
    {
        var rig = Create(Named);

        rig.Push(Account("p", "microsoft-brokered", "work@contoso.example"));

        rig.Model.WindowsAccountOffered.Should().BeTrue();
    }

    [Fact]
    public void With_an_unnamed_listing_any_connected_brokered_account_stops_the_offer()
    {
        var rig = Create(Unnamed);
        rig.Model.WindowsAccountOffered.Should().BeTrue();

        rig.Push(Account("p", "microsoft-brokered", "work@contoso.example"));

        rig.Model.WindowsAccountOffered.Should().BeFalse();
    }

    [Fact]
    public void A_browser_account_with_the_same_email_is_not_offered_because_the_engine_would_refuse_it()
    {
        var rig = Create(Named);

        rig.Push(Account("b", "microsoft", "ME@example.com"));

        rig.Model.WindowsAccountOffered.Should().BeFalse();
    }

    [Fact]
    public void A_browser_account_with_another_email_does_not_stop_the_offer()
    {
        var rig = Create(Named);

        rig.Push(Account("b", "microsoft", "other@example.com"));

        rig.Model.WindowsAccountOffered.Should().BeTrue();
    }

    [Fact]
    public void Only_the_listing_is_called_on_the_broker_before_a_connect()
    {
        var rig = Create(Named);
        rig.Push(Account("g", "google", "you@example.com"));
        rig.Push();

        rig.Source.Calls.Should().Equal("listing");
        rig.Intent.Armed.Should().BeEmpty();
        rig.Engine.Sent.Should().BeEmpty();
    }

    // ---- an ordinary row ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_windows_account_is_an_ordinary_microsoft_row_like_a_browser_one()
    {
        var rig = Create(Named);

        rig.Push(Account("p", "microsoft-brokered", "me@example.com"), Account("b", "microsoft", "other@example.com"));

        rig.Model.ListAccounts.Select(a => a.ConnectionId).Should().Equal("p", "b");
        rig.Model.ListAccounts.Select(a => a.KindName).Should().Equal("Microsoft", "Microsoft");
        rig.Model.ListAccounts.Select(a => a.AutomationName).Should().Equal(
            "Microsoft, me@example.com, Connected", "Microsoft, other@example.com, Connected");
    }

    [Fact]
    public async Task A_windows_account_that_needs_the_person_offers_sign_in_again_in_its_row_and_reconnect_arms_the_windows_account()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com", "authExpired"));

        var row = rig.Model.ListAccounts.Single();
        row.CanSignInAgain.Should().BeTrue();
        row.StatusText.Should().Be("Sign in again");

        await rig.Model.ReauthorizeAsync("p");

        rig.Intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0));
        rig.Engine.Sent.Should().Equal("reconnect:p");
    }

    [Fact]
    public void The_withdrawal_text_applies_to_either_kind_of_microsoft_account_and_not_to_others()
    {
        var rig = Create(Named);
        rig.Push(Account("g", "google", "you@example.com"));
        rig.Model.ShowWithdrawConsent.Should().BeFalse();
        rig.Model.BeginRemove("g");
        rig.Model.RemovalWithdrawText.Should().BeNull();
        rig.Model.CancelRemove();

        rig.Push(Account("g", "google", "you@example.com"), Account("b", "microsoft", "other@example.com"));
        rig.Model.ShowWithdrawConsent.Should().BeTrue();
        rig.Model.BeginRemove("b");
        rig.Model.RemovalWithdrawText.Should().StartWith("To stop TimeTug reading a Microsoft account's calendars");
        rig.Model.CancelRemove();

        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));
        rig.Model.ShowWithdrawConsent.Should().BeTrue();
        rig.Model.BeginRemove("p");
        rig.Model.RemovalWithdrawText.Should().Be(rig.Model.WithdrawConsentText);
    }

    // ---- remembering a removal --------------------------------------------------------------------------------------------------------

    private static async Task Remove(ListRig rig, string id)
    {
        rig.Model.BeginRemove(id);
        await rig.Model.ConfirmRemoveAsync();
    }

    [Fact]
    public async Task Removing_the_matching_brokered_account_records_the_dismissal_once_the_engine_drops_it()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        await Remove(rig, "p");
        rig.Dismissals.IsDismissed.Should().BeFalse("the engine has not said the account is gone");

        rig.Push();

        rig.Dismissals.IsDismissed.Should().BeTrue();
        rig.Dismissals.DismissCalls.Should().Be(1);
        rig.Model.WindowsAccountOffered.Should().BeFalse("nothing is offered again after a removal");
    }

    [Theory]
    [InlineData("unnamed")]
    [InlineData("unavailable")]
    public async Task Removing_a_brokered_account_when_windows_cannot_say_which_is_which_records_the_dismissal(string listing)
    {
        var rig = Create(listing == "unnamed" ? Unnamed : Unavailable);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        await Remove(rig, "p");
        rig.Push();

        rig.Dismissals.IsDismissed.Should().BeTrue();
    }

    [Fact]
    public async Task Removing_a_brokered_account_before_the_listing_answered_records_the_dismissal()
    {
        var rig = Create(gated: true);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        await Remove(rig, "p");
        rig.Push();

        rig.Dismissals.IsDismissed.Should().BeTrue();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("other")]
    public async Task Removing_an_account_that_is_not_the_windows_account_records_nothing(string listing)
    {
        var rig = Create(listing == "none" ? None : Named);
        rig.Push(Account("p", "microsoft-brokered", listing == "none" ? "me@example.com" : "work@contoso.example"));

        await Remove(rig, "p");
        rig.Push();

        rig.Dismissals.IsDismissed.Should().BeFalse();
        rig.Dismissals.DismissCalls.Should().Be(0);
    }

    [Fact]
    public async Task Removing_a_google_or_browser_account_never_records_a_dismissal()
    {
        var rig = Create(Named);
        rig.Push(Account("g", "google", "you@example.com"), Account("b", "microsoft", "me@example.com"));

        await Remove(rig, "g");
        rig.Push(Account("b", "microsoft", "me@example.com"));
        await Remove(rig, "b");
        rig.Push();

        rig.Dismissals.DismissCalls.Should().Be(0);
    }

    [Fact]
    public void Cancelling_the_removal_question_records_nothing()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        rig.Model.BeginRemove("p");
        rig.Model.CancelRemove();
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        rig.Dismissals.DismissCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_refused_removal_records_nothing()
    {
        var rig = Create(Named);
        var account = Account("p", "microsoft-brokered", "me@example.com");
        rig.Push(account);
        await Remove(rig, "p");

        rig.PushActivity(new AccountActivity(false, "Could not remove the account.", "browser"), account);
        rig.Push();

        rig.Dismissals.DismissCalls.Should().Be(0, "the account is gone later for another reason, not because of this removal");
    }

    [Fact]
    public async Task Plus_microsoft_still_adds_the_windows_account_after_a_dismissal()
    {
        var rig = Create(Named, dismissed: true);

        await rig.Model.ConnectAsync("microsoft-brokered:pick");

        rig.Intent.Armed.Should().Equal((BrokerIntent.PickAccount, 0));
        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
    }

    private static ListRig Create(WindowsAccountListing? listing = null, AppState? first = null, bool gated = false, bool withSource = true, bool dismissed = false) =>
        ListRig.Create(listing, first, gated, withSource, dismissed);
}
