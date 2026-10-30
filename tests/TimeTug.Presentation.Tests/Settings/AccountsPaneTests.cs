using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Welcome;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>The Accounts pane's side of <see cref="AccountsViewModel"/>: the connected accounts, signing in again, removing, adding and the waiting state.</summary>
public class AccountsPaneTests
{
    private static readonly AccountActivity Idle = new(false, null, "browser");
    private static readonly AccountActivity Working = new(true, null, "browser");

    private static (FakeEngineClient Engine, AccountsViewModel Model) Create(AppState? first = null)
    {
        var engine = new FakeEngineClient();
        var model = new AccountsViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), TimeProvider.System);
        engine.Push(first ?? States.Empty());
        return (engine, model);
    }

    private static AccountInfo Account(string id, string kind, string name, string status = "ok") => new(id, kind, name, status, 2);

    private static AppState Accounts(params AccountInfo[] accounts) => States.With(accounts);

    // ---- the connected accounts -------------------------------------------------------------------------------------------------

    [Fact]
    public void Each_status_has_the_Macs_words_and_only_an_expired_sign_in_or_a_build_failure_asks_to_sign_in_again()
    {
        var (_, model) = Create(Accounts(
            Account("a", "google", "ok@example.com"),
            Account("b", "microsoft", "expired@example.com", "authExpired"),
            Account("c", "icloud", "off@example.com", "needsPermission"),
            Account("d", "caldav", "down@example.com", "failing"),
            Account("e", "google", "new@example.com") with { StatusKnown = false },
            Account("f", "caldav", "broken@example.com", "failing") with { BuildFailure = true }));

        model.ConnectedAccounts.Select(a => a.StatusText).Should().Equal(
            "Connected", "Sign in again", "Calendar access is off", "Can’t reach this calendar right now", "Connecting…", "Can’t start this account");
        model.ConnectedAccounts.Select(a => a.CanSignInAgain).Should().Equal(false, true, false, false, false, true);
        model.ConnectedAccounts.Select(a => a.NeedsAttention).Should().Equal(false, true, false, false, false, true);
        model.ConnectedAccounts.Select(a => a.Name).Should().Equal("ok@example.com", "expired@example.com", "off@example.com", "down@example.com", "new@example.com", "broken@example.com");
        model.ConnectedAccounts.Select(a => a.KindId).Should().Equal("google", "microsoft", "icloud", "caldav", "google", "caldav");
    }

    [Fact]
    public void A_build_failure_wins_over_the_source_status()
    {
        var (_, model) = Create(Accounts(Account("a", "caldav", "x", "authExpired") with { BuildFailure = true }));
        model.ConnectedAccounts.Single().StatusText.Should().Be("Can’t start this account");
    }

    [Fact]
    public async Task A_healthy_account_never_offers_to_sign_in_again()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "ok@example.com")));
        model.ConnectedAccounts.Single().CanSignInAgain.Should().BeFalse();

        await model.ReauthorizeAsync("a");
        engine.Sent.Should().BeEmpty("a stale click on an account that does not need it sends nothing");
    }

    [Fact]
    public async Task Signing_in_again_asks_the_engine_to_reconnect_that_account()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "ok@example.com"), Account("b", "microsoft", "expired@example.com", "authExpired")));

        await model.ReauthorizeAsync("b");

        engine.Sent.Should().Equal("reconnect:b");
    }

    [Fact]
    public async Task Signing_in_again_and_removing_are_off_while_a_sign_in_runs()
    {
        var (engine, model) = Create(Accounts(Account("b", "microsoft", "expired@example.com", "authExpired")));
        engine.Push(States.With([Account("b", "microsoft", "expired@example.com", "authExpired")], activity: Working));

        model.IsBusy.Should().BeTrue();
        await model.ReauthorizeAsync("b");
        model.BeginRemove("b");

        engine.Sent.Should().BeEmpty();
        model.PendingRemoval.Should().BeNull();
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, "Some repeating events from this link use a rule TimeTug can’t read, so they may not appear.")]
    [InlineData(3, "3 repeating events from this link use a rule TimeTug can’t read, so they may not appear.")]
    public void Repeating_events_the_engine_cannot_read_show_a_notice_on_the_account(int count, string? text)
    {
        var (_, model) = Create(Accounts(Account("a", "icalsub", "Team feed") with { UnreadableRecurrences = count }));
        model.ConnectedAccounts.Single().NoticeText.Should().Be(text);
    }

    [Fact]
    public void Accounts_follow_the_engines_state()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "one@example.com")));
        engine.Push(Accounts(Account("a", "google", "one@example.com"), Account("b", "icloud", "two@example.com")));
        model.ConnectedAccounts.Select(a => a.Name).Should().Equal("one@example.com", "two@example.com");

        engine.Push(Accounts(Account("b", "icloud", "two@example.com")));
        model.ConnectedAccounts.Select(a => a.Name).Should().Equal("two@example.com");
    }

    [Fact]
    public void A_state_that_changes_no_row_does_not_tell_the_page_to_rebuild_it()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "one@example.com")));
        var changes = new List<string?>();
        model.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        engine.Push(Accounts(Account("a", "google", "one@example.com")));
        changes.Should().NotContain(nameof(AccountsViewModel.ConnectedAccounts));

        engine.Push(Accounts(Account("a", "google", "one@example.com", "authExpired")));
        changes.Should().Contain(nameof(AccountsViewModel.ConnectedAccounts));
    }

    [Fact]
    public void Every_row_has_words_for_a_screen_reader()
    {
        var (_, model) = Create(Accounts(Account("b", "microsoft", "expired@example.com", "authExpired")));
        var row = model.ConnectedAccounts.Single();
        row.ActionsAutomationName.Should().Be("expired@example.com actions");
        row.SignInAutomationName.Should().Be("Sign in again to expired@example.com");
        row.RemoveAutomationName.Should().Be("Remove expired@example.com");
        row.AutomationName.Should().Be("Microsoft, expired@example.com, Sign in again");
    }

    // ---- the words of the pane -------------------------------------------------------------------------------------------------

    [Fact]
    public void The_pane_uses_the_Macs_words()
    {
        var (_, model) = Create();
        model.IntroText.Should().Be("Choose where TimeTug reads your calendars from.");
        model.ConnectedTitle.Should().Be("Connected Accounts");
        model.AddTitle.Should().Be("Add an Account");
        model.EmptyText.Should().Be("No internet accounts yet. Add one below.");
    }

    [Fact]
    public void With_no_accounts_the_empty_row_shows_and_removing_the_last_account_goes_back_to_it()
    {
        var (engine, model) = Create(States.Empty());
        model.IsEmpty.Should().BeTrue();

        engine.Push(Accounts(Account("a", "google", "one@example.com")));
        model.IsEmpty.Should().BeFalse();

        engine.Push(States.Empty());
        model.IsEmpty.Should().BeTrue("the engine's state drives the list");
        model.ConnectedAccounts.Should().BeEmpty();
    }

    [Fact]
    public void Nothing_is_called_empty_before_the_first_state()
    {
        var engine = new FakeEngineClient();
        var model = new AccountsViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), TimeProvider.System);
        model.IsEmpty.Should().BeFalse();
    }

    // ---- removing ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Remove_asks_first_naming_the_account_and_what_is_lost_and_sends_nothing_yet()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "one@example.com")));

        model.BeginRemove("a");

        model.PendingRemoval.Should().NotBeNull();
        model.RemovalTitle.Should().Be("Remove one@example.com?");
        model.RemovalBody.Should().Be("Its calendars will no longer appear and their Tug and visibility choices are forgotten.");
        model.RemovalConfirmText.Should().Be("Remove");
        model.RemovalCancelText.Should().Be("Cancel");
        engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Confirming_removes_that_account_through_the_engine_and_the_state_drives_the_list()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "one@example.com"), Account("b", "icloud", "two@example.com")));
        model.BeginRemove("b");

        await model.ConfirmRemoveAsync();

        engine.Sent.Should().Equal("remove:b");
        model.PendingRemoval.Should().BeNull();
        model.ConnectedAccounts.Should().HaveCount(2, "the list changes when the engine says so, not before");
        engine.Push(Accounts(Account("a", "google", "one@example.com")));
        model.ConnectedAccounts.Select(a => a.ConnectionId).Should().Equal("a");
    }

    [Fact]
    public async Task Removing_the_last_account_ends_in_the_empty_state()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "one@example.com")));
        model.BeginRemove("a");
        await model.ConfirmRemoveAsync();
        engine.Push(States.Empty());

        engine.Sent.Should().Equal("remove:a");
        model.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelling_forgets_the_question_and_sends_nothing()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "one@example.com")));
        model.BeginRemove("a");

        model.CancelRemove();
        await model.ConfirmRemoveAsync();

        model.PendingRemoval.Should().BeNull();
        engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task An_account_that_vanishes_while_the_question_is_open_cancels_it()
    {
        var (engine, model) = Create(Accounts(Account("a", "google", "one@example.com")));
        model.BeginRemove("a");

        engine.Push(States.Empty());
        await model.ConfirmRemoveAsync();

        model.PendingRemoval.Should().BeNull();
        engine.Sent.Should().BeEmpty("there is nothing left to remove");
    }

    [Fact]
    public void Asking_to_remove_an_account_that_is_not_listed_does_nothing()
    {
        var (_, model) = Create(Accounts(Account("a", "google", "one@example.com")));
        model.BeginRemove("zzz");
        model.PendingRemoval.Should().BeNull();
    }

    // ---- adding ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_buttons_to_add_come_from_the_engines_kinds_and_an_unconfigured_one_is_disabled_with_the_reason()
    {
        var kinds = new List<AccountKindInfo>(States.Kinds) { [1] = new("microsoft", "Microsoft", false, "oauth") };
        var (engine, model) = Create(States.Empty(kinds));

        model.Providers.Select(p => p.Name).Should().Equal("Google", "Microsoft", "iCloud", "Other (CalDAV)");
        var microsoft = model.Providers[1];
        microsoft.IsEnabled.Should().BeFalse();
        microsoft.Hint.Should().Be("Microsoft sign-in isn’t set up in this build.");
        model.Providers.Where(p => p.Id != "microsoft").Should().OnlyContain(p => p.IsEnabled);

        await model.ConnectAsync("microsoft");
        engine.Sent.Should().BeEmpty();
        await model.ConnectAsync("icloud");
        engine.Sent.Should().Equal("add:icloud");
    }

    [Fact]
    public void A_notice_says_so_when_some_kinds_are_unavailable_in_this_build_and_not_otherwise()
    {
        var kinds = new List<AccountKindInfo>(States.Kinds) { [1] = new("microsoft", "Microsoft", false, "oauth") };
        var (engine, model) = Create(States.Empty(kinds));
        model.UnavailableNotice.Should().Be("This build is missing configuration for some account types, so they can’t be added right now.");

        engine.Push(States.Empty());
        model.UnavailableNotice.Should().BeNull();
    }

    [Fact]
    public void The_system_calendar_kind_is_not_a_tile_and_does_not_count_as_unavailable()
    {
        var kinds = new List<AccountKindInfo>(States.Kinds) { new("eventkit", "Apple Calendar", false, "system") };
        var (_, model) = Create(States.Empty(kinds));
        model.Providers.Select(p => p.Id).Should().NotContain("eventkit");
        model.UnavailableNotice.Should().BeNull();
    }

    [Fact]
    public void An_unconfigured_tile_reads_as_unavailable_to_a_screen_reader()
    {
        var kinds = new List<AccountKindInfo>(States.Kinds) { [1] = new("microsoft", "Microsoft", false, "oauth") };
        var (_, model) = Create(States.Empty(kinds));
        model.Providers[1].AutomationName.Should().Be("Microsoft, unavailable in this build");
        model.Providers[0].AutomationName.Should().Be("Connect Google");
    }

    // ---- signing in --------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_sign_in_failure_shows_beside_that_provider_and_the_pane_does_not_repeat_it_elsewhere()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");
        engine.Push(States.Empty(activity: Working));
        engine.Push(States.Empty(activity: new AccountActivity(false, "Sign-in failed: access denied.", "browser")));

        model.Providers[0].Error.Should().Contain("Google");
        model.Providers[0].ErrorDetail.Should().Be("Sign-in failed: access denied.");
        model.ErrorText.Should().BeNull("the provider already shows it");
    }

    [Fact]
    public void An_engine_error_that_no_provider_shows_is_shown_in_the_pane()
    {
        var (_, model) = Create(States.With([Account("b", "microsoft", "expired@example.com", "authExpired")], activity: new AccountActivity(false, "The sign-in expired.", "browser")));
        model.ErrorText.Should().Be("The sign-in expired.");
    }

    [Fact]
    public async Task While_signing_in_the_pane_waits_with_the_providers_words_and_cancel_sends_account_cancel()
    {
        var (engine, model) = Create();
        await model.ConnectAsync("google");

        model.IsBusy.Should().BeTrue();
        model.WaitingText.Should().Be("Waiting for your browser…");
        model.CancelText.Should().Be("Cancel");
        model.CancelAutomationName.Should().Be("Cancel signing in");
        model.Providers.Should().OnlyContain(p => !p.IsEnabled);

        await model.CancelAsync();

        engine.Sent.Should().Equal("add:google", "cancel");
        model.IsBusy.Should().BeFalse();
        model.Providers.Should().OnlyContain(p => p.IsEnabled);
    }

    [Fact]
    public async Task Signing_in_again_waits_too_when_the_engine_is_working()
    {
        var expired = Account("b", "microsoft", "expired@example.com", "authExpired");
        var (engine, model) = Create(Accounts(expired));

        await model.ReauthorizeAsync("b");
        engine.Push(States.With([expired], activity: new AccountActivity(true, null, "browser")));

        model.IsBusy.Should().BeTrue();
        model.WaitingText.Should().Be("Waiting for your browser…");
    }

    // ---- deep links --------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_search_ids_of_the_pane_are_the_catalogs()
    {
        AccountsViewModel.Ids.Accounts.Should().Be("accounts");
        AccountsViewModel.Ids.Diagnostics.Should().Be("diagnostics");
        AccountsViewModel.Ids.AccountPrefix.Should().Be("account:");
    }
}
