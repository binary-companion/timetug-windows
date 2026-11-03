using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Accounts;
using TimeTug.Presentation.Tests.Settings;
using TimeTug.Presentation.Welcome;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Welcome;

/// <summary>The first-run window: an intro step, the accounts list, and the Continue rule that adds the Windows account (Q1, Q4, Q5; D10, D16, D17; spec A1.2).</summary>
public class WelcomeListFlowTests
{
    private sealed record Rig(FakeEngineClient Engine, WelcomeViewModel Model, AccountsBrokeredTests.FakeIntent Intent, FakeListing Source, FakeDismissals Dismissals);

    private static Rig Create(WindowsAccountListing? listing = null, bool gated = false, AppState? first = null, bool dismissed = false)
    {
        var engine = new FakeEngineClient();
        var model = new WelcomeViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), new FakeTimeProvider(States.Noon));
        var intent = new AccountsBrokeredTests.FakeIntent(engine.Sent);
        model.Accounts.BrokerIntents = intent;
        var dismissals = new FakeDismissals(dismissed);
        model.Accounts.Dismissals = dismissals;
        var source = new FakeListing(listing ?? Named);
        if (gated)
        {
            source.Gate = new TaskCompletionSource<WindowsAccountListing>();
        }

        engine.Push(first ?? ListRig.Empty());
        model.Accounts.WindowsAccounts = source;
        return new Rig(engine, model, intent, source, dismissals);
    }

    private static async Task Start(Rig rig)
    {
        rig.Model.GetStartedCommand.Execute(null);
        await Task.CompletedTask;
    }

    [Fact]
    public void The_flow_starts_on_the_intro_step()
    {
        var rig = Create();

        rig.Model.Step.Should().Be(WelcomeStep.Intro);
        rig.Model.IsIntroStep.Should().BeTrue();
        rig.Model.IsConnectStep.Should().BeFalse();
        rig.Model.IsConnectedStep.Should().BeFalse();
    }

    [Fact]
    public async Task Get_started_moves_to_connect_and_the_listing_started_on_construction()
    {
        var rig = Create();
        rig.Source.Calls.Should().Equal("listing");

        await Start(rig);

        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Source.Calls.Should().Equal("listing");
        rig.Model.Accounts.WindowsAccountOffered.Should().BeTrue("the answer was ready before step 2 opened");
        rig.Model.Accounts.ListAccounts.Should().BeEmpty("nothing is added until Continue");
        rig.Model.Accounts.ShowListEmptyText.Should().BeTrue();
    }

    [Fact]
    public async Task Get_started_does_nothing_after_the_intro()
    {
        var rig = Create();
        await Start(rig);
        rig.Model.GetStartedCommand.CanExecute(null).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Continue_is_disabled_while_the_listing_is_pending_and_a_late_answer_enables_it_without_changing_the_step(bool withAnAccount)
    {
        var rig = Create(gated: true);
        await Start(rig);
        if (withAnAccount)
        {
            rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));
        }

        var changed = new List<string?>();
        ((INotifyPropertyChanged)rig.Model).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Model.CanContinue.Should().BeFalse("Continue never goes on before the Windows offer is known");
        rig.Model.ContinueCommand.CanExecute(null).Should().BeFalse();

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => rig.Model.CanContinue);

        rig.Model.CanContinue.Should().BeTrue();
        rig.Model.ContinueCommand.CanExecute(null).Should().BeTrue();
        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        changed.Should().Contain(nameof(WelcomeViewModel.CanContinue));
    }

    [Fact]
    public async Task Continue_connects_the_windows_account_and_goes_to_connected_with_an_ordinary_row()
    {
        var rig = Create();
        await Start(rig);

        await Press(rig.Model.ContinueCommand);

        rig.Intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0));
        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
        rig.Model.Step.Should().Be(WelcomeStep.Connect, "Windows has not answered yet");
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Model.Step.Should().Be(WelcomeStep.Connect);

        rig.Engine.Push(With(Idle, Account("p", "microsoft-brokered", "me@example.com")));

        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Model.Accounts.Accounts.Should().ContainSingle().Which.Summary.Should().Be("Microsoft · me@example.com · 2 calendars");
        rig.Model.Accounts.ListAccounts.Should().ContainSingle().Which.AutomationName.Should().Be("Microsoft, me@example.com, Connected");
    }

    [Fact]
    public async Task Declining_stays_on_connect_with_no_error_and_the_link()
    {
        var rig = Create();
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(ListRig.Empty(Idle));

        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Model.Accounts.ListAccounts.Should().BeEmpty();
        rig.Model.Accounts.ErrorText.Should().BeNull();
        rig.Model.Accounts.SignInFailureText.Should().BeNull();
        rig.Model.Accounts.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.Accounts.IsBusy.Should().BeFalse();
        rig.Model.CanContinue.Should().BeTrue("the person may press Continue again");
        rig.Intent.Current.Should().Be(BrokerIntent.WindowsAccount);
        rig.Dismissals.DismissCalls.Should().Be(0, "a decline is not remembered");
    }

    // ---- the note under the list: what Continue does (the owner pressed "+" instead of Continue and never learned it adds the Windows account) ----

    [Fact]
    public async Task While_the_windows_account_is_offered_a_note_says_what_continue_does_and_names_the_account()
    {
        var rig = Create(Named);
        await Start(rig);

        rig.Model.ShowConnectNote.Should().BeTrue();
        rig.Model.ConnectNote.Should().Contain("Continue").And.Contain("sign in to Windows").And.Contain("me@example.com");
    }

    [Fact]
    public async Task The_note_does_not_name_an_account_when_windows_names_none_in_particular()
    {
        var rig = Create(Unnamed);
        await Start(rig);

        rig.Model.ShowConnectNote.Should().BeTrue();
        rig.Model.ConnectNote.Should().Contain("Continue").And.NotContain("@");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("unavailable")]
    public async Task There_is_no_note_when_nothing_is_offered(string listing)
    {
        var rig = Create(listing == "none" ? None : Unavailable);
        await Start(rig);

        rig.Model.ShowConnectNote.Should().BeFalse();
        rig.Model.ConnectNote.Should().BeNull();
    }

    [Fact]
    public async Task There_is_no_note_after_a_recorded_removal()
    {
        var rig = Create(dismissed: true);
        await Start(rig);

        rig.Model.ShowConnectNote.Should().BeFalse();
    }

    [Fact]
    public async Task A_late_listing_shows_the_note_and_tells_the_window()
    {
        var rig = Create(gated: true);
        await Start(rig);
        rig.Model.ShowConnectNote.Should().BeFalse("the listing has not answered");
        var changed = new List<string?>();
        rig.Model.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => rig.Model.ShowConnectNote);

        rig.Model.ShowConnectNote.Should().BeTrue();
        changed.Should().Contain(nameof(WelcomeViewModel.ShowConnectNote)).And.Contain(nameof(WelcomeViewModel.ConnectNote));
    }

    [Fact]
    public async Task The_note_is_hidden_while_windows_is_being_asked_and_after_a_decline_it_says_so_and_how_to_try_again()
    {
        var rig = Create(Named);
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Model.ShowConnectNote.Should().BeFalse("the waiting line is on screen");

        rig.Engine.Push(ListRig.Empty(Idle));

        rig.Model.ShowConnectNote.Should().BeTrue();
        rig.Model.ConnectNote.Should().Contain("Windows didn't").And.Contain("Continue").And.NotContain("sign in to Windows");
    }

    [Fact]
    public async Task After_a_decline_the_note_goes_once_another_account_is_connected_because_continue_then_goes_on_without_asking()
    {
        var rig = Create(Named);
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(ListRig.Empty(Idle));
        rig.Model.ShowConnectNote.Should().BeTrue();
        await Press(rig.Model.Accounts.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));

        rig.Model.ShowConnectNote.Should().BeFalse("Continue no longer asks Windows, so a line promising a retry would be wrong");
    }

    [Fact]
    public async Task The_note_goes_when_the_account_is_connected()
    {
        var rig = Create(Named);
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(With(Idle, Account("p", "microsoft-brokered", "me@example.com")));

        rig.Model.ShowConnectNote.Should().BeFalse();
    }

    [Fact]
    public async Task Declining_then_Continue_with_an_empty_list_asks_windows_again()
    {
        var rig = Create();
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(ListRig.Empty(Idle));
        rig.Engine.Sent.Clear();

        await Press(rig.Model.ContinueCommand);

        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
        rig.Model.Step.Should().Be(WelcomeStep.Connect);
    }

    [Fact]
    public async Task Declining_then_Continue_with_an_account_in_the_list_goes_on_without_asking()
    {
        var rig = Create();
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(ListRig.Empty(Idle));
        await Press(rig.Model.Accounts.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));
        rig.Engine.Sent.Clear();
        rig.Intent.Armed.Clear();

        await Press(rig.Model.ContinueCommand);

        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
        rig.Intent.Armed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failure_stays_on_connect_and_shows_the_engines_text_and_the_link()
    {
        var rig = Create();
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(ListRig.Empty(new AccountActivity(false, "Sign-in failed (network).", "browser")));

        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Model.Accounts.SignInFailureDetail.Should().Be("Sign-in failed (network).");
        rig.Model.Accounts.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.CanContinue.Should().BeTrue();
    }

    [Fact]
    public async Task Declining_then_a_later_sign_in_does_not_leave_a_stale_continue_behind()
    {
        var rig = Create();
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(ListRig.Empty(Idle));

        await rig.Model.Accounts.ConnectAsync("google");
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));

        rig.Model.Step.Should().Be(WelcomeStep.Connect, "an account added with + never advances the window; only Continue does");
    }

    [Fact]
    public async Task Continue_with_the_windows_account_already_connected_makes_no_broker_call()
    {
        var rig = Create();
        await Start(rig);
        rig.Engine.Push(With(Idle, Account("p", "microsoft-brokered", "me@example.com")));
        rig.Engine.Sent.Clear();
        rig.Intent.Armed.Clear();
        rig.Model.CanContinue.Should().BeTrue();

        await Press(rig.Model.ContinueCommand);

        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
        rig.Intent.Armed.Should().BeEmpty();
        rig.Source.Calls.Should().Equal("listing");
    }

    [Fact]
    public async Task Continue_with_a_google_account_in_the_list_still_asks_windows_once()
    {
        var rig = Create();
        await Start(rig);
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));
        rig.Engine.Sent.Clear();

        await Press(rig.Model.ContinueCommand);

        rig.Engine.Sent.Should().Equal("add:microsoft-brokered");
        rig.Intent.Armed.Should().Equal((BrokerIntent.WindowsAccount, 0));
    }

    [Fact]
    public async Task Plus_adds_an_account_and_stays_on_connect()
    {
        var rig = Create();
        await Start(rig);

        await Press(rig.Model.Accounts.MenuEntries.Single(e => e.Name == "Google").ConnectCommand);
        rig.Engine.Push(ListRig.Empty(Working));
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));

        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Model.Accounts.ListAccounts.Should().ContainSingle();
        rig.Model.CanContinue.Should().BeTrue();
    }

    [Fact]
    public async Task No_broker_call_other_than_the_listing_happens_before_Continue()
    {
        var rig = Create();
        await Start(rig);
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));

        rig.Source.Calls.Should().Equal("listing");
        rig.Intent.Armed.Should().BeEmpty();
        rig.Engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_local_only_windows_account_disables_Continue_with_an_empty_list_and_Skip_closes()
    {
        var rig = Create(None);
        await Start(rig);
        var closed = 0;
        rig.Model.CloseRequested += (_, _) => closed++;

        rig.Model.Accounts.ShowListEmptyText.Should().BeTrue();
        rig.Model.CanContinue.Should().BeFalse();

        await Press(rig.Model.SkipCommand);

        closed.Should().Be(1);
        rig.Engine.Sent.Should().BeEmpty();
        rig.Intent.Armed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_local_only_windows_account_with_a_google_account_continues_without_asking()
    {
        var rig = Create(None);
        await Start(rig);
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));

        rig.Model.CanContinue.Should().BeTrue();
        await Press(rig.Model.ContinueCommand);

        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recorded_dismissal_makes_no_listing_call_and_Continue_stays_disabled_with_an_empty_list()
    {
        var rig = Create(dismissed: true);
        await Start(rig);

        rig.Source.Calls.Should().BeEmpty();
        rig.Model.CanContinue.Should().BeFalse();
        rig.Model.Accounts.ShowListEmptyText.Should().BeTrue();
    }

    [Fact]
    public async Task A_browser_account_with_the_windows_email_enables_Continue_through_the_list_and_asks_nothing()
    {
        var rig = Create();
        await Start(rig);

        rig.Engine.Push(With(Idle, Account("b", "microsoft", "me@example.com")));

        rig.Model.Accounts.WindowsAccountOffered.Should().BeFalse();
        rig.Model.CanContinue.Should().BeTrue("the browser account is in the list");
        await Press(rig.Model.ContinueCommand);
        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
    }
}
