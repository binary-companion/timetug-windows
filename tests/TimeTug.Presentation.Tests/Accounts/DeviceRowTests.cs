using System.ComponentModel;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Accounts;

/// <summary>The pinned "This PC's Windows account" row (spec D1, D3, D7 to D9; L1, L6).</summary>
public class DeviceRowTests
{
    private const string PreClick = "Windows will ask you to let TimeTug read your calendars when you press Continue. TimeTug stores no password and no sign-in token.";

    // ---- visibility and the default switch ----------------------------------------------------------------------------------------

    [Fact]
    public void Listing_none_hides_the_row()
    {
        var rig = Create(None, pending: true);
        rig.Model.DeviceRow.IsVisible.Should().BeFalse();
        rig.Source.Calls.Should().Equal("listing");
    }

    [Fact]
    public void Listing_unavailable_hides_the_row() => Create(Unavailable, pending: true).Model.DeviceRow.IsVisible.Should().BeFalse();

    [Fact]
    public void Without_a_windows_account_source_the_row_is_hidden() => Create(withSource: false).Model.DeviceRow.IsVisible.Should().BeFalse();

    [Fact]
    public void An_unconfigured_brokered_kind_hides_the_row_even_when_windows_lists_an_account()
    {
        var kinds = Kinds.Select(k => k.Id == "microsoft-brokered" ? k with { IsConfigured = false } : k).ToList();
        var rig = Create(Named, pending: true, first: TimeTug.Presentation.Tests.Welcome.States.Empty(kinds));
        rig.Model.DeviceRow.IsVisible.Should().BeFalse();
    }

    [Fact]
    public void One_named_account_shows_the_email_and_defaults_on()
    {
        var row = Create(Named, pending: true).Model.DeviceRow;

        row.IsVisible.Should().BeTrue();
        row.Title.Should().Be("This PC's Windows account");
        row.Email.Should().Be("me@example.com");
        row.Description.Should().Be(PreClick);
        row.IsOn.Should().BeTrue();
        row.IsSwitchEnabled.Should().BeTrue();
        row.SwitchAutomationName.Should().Be("Use this PC's Windows account");
        row.NeedsSignIn.Should().BeFalse();
    }

    [Fact]
    public void Unnamed_accounts_show_the_row_without_an_email_and_on()
    {
        var row = Create(Unnamed, pending: true).Model.DeviceRow;

        row.IsVisible.Should().BeTrue();
        row.Email.Should().BeNull();
        row.IsOn.Should().BeTrue();
        row.Description.Should().Be(PreClick);
    }

    [Fact]
    public async Task A_late_listing_shows_the_row_in_place()
    {
        var rig = Create(pending: true, gated: true);
        rig.Model.DeviceRow.IsVisible.Should().BeFalse();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)rig.Model.DeviceRow).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => changes.Contains(nameof(DeviceRowModelProbe.IsVisible)));

        rig.Model.DeviceRow.IsVisible.Should().BeTrue();
    }

    // ---- Settings mode ------------------------------------------------------------------------------------------------------------

    [Fact]
    public void In_settings_the_switch_shows_the_engines_truth()
    {
        var rig = Create(Named);
        rig.Model.DeviceRow.IsOn.Should().BeFalse("nothing is connected");
        rig.Model.DeviceRow.Description.Should().Contain("Windows will ask you to let TimeTug read your calendars.").And.NotContain("Continue");

        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));
        rig.Model.DeviceRow.IsOn.Should().BeTrue();
        rig.Model.DeviceRow.StatusText.Should().Be("Connected");

        rig.Push();
        rig.Model.DeviceRow.IsOn.Should().BeFalse();
        rig.Model.DeviceRow.StatusText.Should().BeEmpty();
    }

    [Theory]
    [InlineData("me@example.com")]
    [InlineData("Me@Example.com")]
    [InlineData("ME@EXAMPLE.COM")]
    public void A_connected_brokered_account_with_the_listed_email_shows_only_in_the_row(string connectedEmail)
    {
        var rig = Create(Named);

        rig.Push(Account("p", "microsoft-brokered", connectedEmail));

        rig.Model.ListAccounts.Should().BeEmpty();
        rig.Model.DeviceRow.IsOn.Should().BeTrue();
        rig.Model.ConnectedAccounts.Should().ContainSingle("the shipped pane's list is unchanged");
    }

    [Fact]
    public void A_connected_brokered_account_with_another_email_stays_a_list_row()
    {
        var rig = Create(Named);

        rig.Push(Account("p", "microsoft-brokered", "work@contoso.example"));

        rig.Model.ListAccounts.Should().ContainSingle().Which.IsOnThisPc.Should().BeTrue();
        rig.Model.DeviceRow.IsOn.Should().BeFalse();
    }

    [Theory]
    [InlineData("none")]
    [InlineData("unavailable")]
    public void A_connected_brokered_account_when_the_row_is_hidden_stays_a_list_row(string listing)
    {
        var rig = Create(listing == "none" ? None : Unavailable);

        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        rig.Model.DeviceRow.IsVisible.Should().BeFalse();
        rig.Model.ListAccounts.Should().ContainSingle().Which.ConnectionId.Should().Be("p");
    }

    [Fact]
    public async Task A_connected_brokered_account_never_disappears_when_the_listing_is_late_or_changes()
    {
        var rig = Create(pending: true, gated: true);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));
        rig.Model.ListAccounts.Should().ContainSingle("the listing has not answered, so the row is hidden and the account is an ordinary row");

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => rig.Model.DeviceRow.IsVisible);

        rig.Model.ListAccounts.Should().BeEmpty("now the pinned row stands for it");
        rig.Model.ConnectedAccounts.Should().ContainSingle();
    }

    [Fact]
    public void A_connected_brokered_account_when_the_listing_is_unnamed_stays_a_list_row()
    {
        var rig = Create(Unnamed);

        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        rig.Model.ListAccounts.Should().ContainSingle();
        rig.Model.DeviceRow.IsVisible.Should().BeTrue();
        rig.Model.DeviceRow.IsOn.Should().BeFalse();
    }

    [Fact]
    public void A_browser_account_with_the_same_email_disables_the_row_and_shows_the_conflict_line()
    {
        var rig = Create(Named, pending: true);

        rig.Push(Account("b", "microsoft", "ME@example.com"));

        var row = rig.Model.DeviceRow;
        row.IsVisible.Should().BeTrue();
        row.IsOn.Should().BeFalse();
        row.IsSwitchEnabled.Should().BeFalse();
        row.Description.Should().Be("me@example.com is already connected through your browser. Remove it to use Windows sign-in.");
        rig.Model.ListAccounts.Should().ContainSingle().Which.ConnectionId.Should().Be("b");
    }

    [Fact]
    public async Task The_switch_is_off_while_a_sign_in_runs()
    {
        var rig = Create(Named);
        rig.Model.DeviceRow.IsSwitchEnabled.Should().BeTrue();

        await rig.Model.ConnectAsync("google");
        rig.PushActivity(Working);

        rig.Model.DeviceRow.IsSwitchEnabled.Should().BeFalse();
        await rig.Model.DeviceRow.SetOnAsync(true);
        rig.Engine.Sent.Should().Equal(["add:google"], "a stale toggle sends nothing");
    }

    // ---- what the switch does -------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task In_the_welcome_window_switching_off_makes_no_broker_call()
    {
        var rig = Create(Named, pending: true);

        await rig.Model.DeviceRow.SetOnAsync(false);

        rig.Model.DeviceRow.IsOn.Should().BeFalse();
        rig.Intent.Armed.Should().BeEmpty();
        rig.Engine.Sent.Should().BeEmpty();
        rig.Source.Calls.Should().Equal("listing");

        await rig.Model.DeviceRow.SetOnAsync(true);
        rig.Model.DeviceRow.IsOn.Should().BeTrue();
        rig.Engine.Sent.Should().BeEmpty("the switch is only a pending choice until Continue");
    }

    [Fact]
    public async Task In_settings_switching_on_arms_the_windows_account_and_connects()
    {
        var rig = Create(Named);

        await rig.Model.DeviceRow.SetOnAsync(true);

        rig.Intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0));
        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
    }

    [Fact]
    public async Task In_settings_switching_on_when_already_connected_does_nothing()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        await rig.Model.DeviceRow.SetOnAsync(true);

        rig.Engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task In_settings_switching_off_asks_the_removal_question_with_the_withdrawal_text()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));

        await rig.Model.DeviceRow.SetOnAsync(false);

        rig.Model.PendingRemoval.Should().NotBeNull();
        rig.Model.RemovalTitle.Should().Be("Remove me@example.com?");
        rig.Model.RemovalBody.Should().Be("Its calendars will no longer appear and their Tug and visibility choices are forgotten.");
        rig.Model.RemovalWithdrawText.Should().StartWith("To stop TimeTug reading a Microsoft account's calendars");
        rig.Engine.Sent.Should().BeEmpty("nothing is removed until the question is confirmed");
        rig.Model.DeviceRow.IsOn.Should().BeTrue("the row changes when the engine's state says so");
    }

    [Fact]
    public async Task Cancelling_the_removal_question_re_announces_the_switch_so_the_control_follows_the_model()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));
        await rig.Model.DeviceRow.SetOnAsync(false);
        var changes = new List<string?>();
        ((INotifyPropertyChanged)rig.Model.DeviceRow).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        rig.Model.CancelRemove();

        changes.Should().Contain(nameof(DeviceRowModelProbe.IsOn));
        rig.Model.DeviceRow.IsOn.Should().BeTrue();
    }

    [Fact]
    public async Task A_refused_removal_re_announces_the_switch_so_the_control_follows_the_model()
    {
        var rig = Create(Named);
        var pinned = Account("p", "microsoft-brokered", "me@example.com");
        rig.Push(pinned);
        await rig.Model.DeviceRow.SetOnAsync(false);
        await rig.Model.ConfirmRemoveAsync();
        var changes = new List<string?>();
        ((INotifyPropertyChanged)rig.Model.DeviceRow).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        rig.PushActivity(new AccountActivity(false, "Could not remove the account.", "browser"), pinned);

        rig.Model.DeviceRow.IsOn.Should().BeTrue();
        changes.Should().Contain(nameof(DeviceRowModelProbe.IsOn));
    }

    [Fact]
    public async Task A_failed_switch_on_re_announces_the_switch_when_the_attempt_ends()
    {
        var rig = Create(Named);
        await rig.Model.DeviceRow.SetOnAsync(true);
        rig.PushActivity(Working);
        var changes = new List<string?>();
        ((INotifyPropertyChanged)rig.Model.DeviceRow).PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        rig.PushActivity(new AccountActivity(false, "Sign-in failed (network).", "browser"));

        rig.Model.DeviceRow.IsOn.Should().BeFalse();
        changes.Should().Contain(nameof(DeviceRowModelProbe.IsOn));
    }

    [Fact]
    public async Task Confirming_the_removal_sends_remove_only_and_asks_nothing_of_the_broker()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com"));
        await rig.Model.DeviceRow.SetOnAsync(false);

        await rig.Model.ConfirmRemoveAsync();

        rig.Engine.Sent.Should().Equal("remove:p");
        rig.Intent.Armed.Should().BeEmpty();
        rig.Source.Calls.Should().Equal("listing");

        rig.Push();
        rig.Model.DeviceRow.IsOn.Should().BeFalse();
    }

    [Fact]
    public async Task The_row_offers_sign_in_again_when_the_pinned_account_needs_it_and_reconnect_arms_the_windows_account()
    {
        var rig = Create(Named);
        rig.Push(Account("p", "microsoft-brokered", "me@example.com", "authExpired"));

        var row = rig.Model.DeviceRow;
        row.NeedsSignIn.Should().BeTrue();
        row.StatusText.Should().Be("Sign in again");
        row.IsOn.Should().BeTrue();

        await Press(row.SignInAgainCommand);

        rig.Intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0));
        rig.Engine.Sent.Should().Equal("reconnect:p");
    }

    [Fact]
    public void Only_the_listing_is_called_on_the_broker_before_a_connect()
    {
        var rig = Create(Named, pending: true);
        rig.Push(Account("g", "google", "you@example.com"));
        rig.Push();

        rig.Source.Calls.Should().Equal("listing");
        rig.Intent.Armed.Should().BeEmpty();
        rig.Engine.Sent.Should().BeEmpty();
    }

    /// <summary>Names of <see cref="DeviceRowModel"/>'s properties, so a rename breaks the build here rather than the assertion silently.</summary>
    private static class DeviceRowModelProbe
    {
        public const string IsVisible = nameof(DeviceRowModel.IsVisible);
        public const string IsOn = nameof(DeviceRowModel.IsOn);
    }
}
