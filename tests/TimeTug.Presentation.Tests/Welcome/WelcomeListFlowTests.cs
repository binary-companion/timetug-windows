using System.ComponentModel;
using Microsoft.Extensions.Time.Testing;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Accounts;
using TimeTug.Presentation.Tests.Settings;
using TimeTug.Presentation.Welcome;
using static TimeTug.Presentation.Tests.Accounts.ListRig;

namespace TimeTug.Presentation.Tests.Welcome;

/// <summary>The first-run window: an intro step, the accounts list with the pinned Windows row, and the Continue rule (Q1, Q4, Q5; D1, D9, D10).</summary>
public class WelcomeListFlowTests
{
    private sealed record Rig(FakeEngineClient Engine, WelcomeViewModel Model, AccountsBrokeredTests.FakeIntent Intent, FakeListing Source);

    private static Rig Create(WindowsAccountListing? listing = null, bool gated = false, AppState? first = null)
    {
        var engine = new FakeEngineClient();
        var model = new WelcomeViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), new FakeTimeProvider(States.Noon));
        var intent = new AccountsBrokeredTests.FakeIntent(engine.Sent);
        model.Accounts.BrokerIntents = intent;
        var source = new FakeListing(listing ?? Named);
        if (gated)
        {
            source.Gate = new TaskCompletionSource<WindowsAccountListing>();
        }

        engine.Push(first ?? ListRig.Empty());
        model.Accounts.WindowsAccounts = source;
        return new Rig(engine, model, intent, source);
    }

    private static async Task Start(Rig rig)
    {
        rig.Model.GetStartedCommand.Execute(null);
        await Task.CompletedTask;
    }

    [Fact]
    public void List_flow_starts_on_the_intro_step()
    {
        var rig = Create();

        rig.Model.Step.Should().Be(WelcomeStep.Intro);
        rig.Model.IsIntroStep.Should().BeTrue();
        rig.Model.IsConnectStep.Should().BeFalse();
        rig.Model.IsConnectedStep.Should().BeFalse();
        rig.Model.Accounts.DeviceRowIsPending.Should().BeTrue();
    }

    [Fact]
    public async Task Get_started_moves_to_connect_and_the_listing_started_on_construction()
    {
        var rig = Create();
        rig.Source.Calls.Should().Equal("listing");

        await Start(rig);

        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Model.IsConnectStep.Should().BeTrue();
        rig.Source.Calls.Should().Equal("listing");
        rig.Model.Accounts.DeviceRow.IsVisible.Should().BeTrue("the answer was ready before step 2 opened");
    }

    [Fact]
    public async Task Get_started_does_nothing_after_the_intro()
    {
        var rig = Create();
        await Start(rig);
        rig.Model.GetStartedCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Continue_is_disabled_while_the_listing_is_pending_and_a_late_answer_enables_it_without_changing_the_step()
    {
        var rig = Create(gated: true);
        await Start(rig);
        var changed = new List<string?>();
        ((INotifyPropertyChanged)rig.Model).PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        rig.Model.CanContinue.Should().BeFalse();
        rig.Model.ContinueCommand.CanExecute(null).Should().BeFalse();
        rig.Model.Accounts.DeviceRow.IsVisible.Should().BeFalse();

        rig.Source.Gate!.SetResult(Named);
        await Eventually(() => rig.Model.CanContinue);

        rig.Model.CanContinue.Should().BeTrue();
        rig.Model.ContinueCommand.CanExecute(null).Should().BeTrue();
        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        changed.Should().Contain(nameof(WelcomeViewModel.CanContinue));
    }

    [Fact]
    public async Task Continue_is_disabled_with_the_switch_off_and_no_account()
    {
        var rig = Create();
        await Start(rig);
        rig.Model.CanContinue.Should().BeTrue();

        await rig.Model.Accounts.DeviceRow.SetOnAsync(false);

        rig.Model.CanContinue.Should().BeFalse();
        rig.Model.ContinueCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task Continue_with_the_switch_on_connects_the_windows_account_and_goes_to_connected()
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
    }

    [Fact]
    public async Task Declining_stays_on_connect_with_the_switch_on_no_error_and_the_link()
    {
        var rig = Create();
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(ListRig.Empty(Idle));

        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Model.Accounts.DeviceRow.IsOn.Should().BeTrue();
        rig.Model.Accounts.ErrorText.Should().BeNull();
        rig.Model.Accounts.SignInFailureText.Should().BeNull();
        rig.Model.Accounts.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.Accounts.IsBusy.Should().BeFalse();
        rig.Model.CanContinue.Should().BeTrue("the person may press Continue again");
        rig.Intent.Current.Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public async Task A_failure_stays_on_connect_and_shows_the_engines_text()
    {
        var rig = Create();
        await Start(rig);
        await Press(rig.Model.ContinueCommand);
        rig.Engine.Push(ListRig.Empty(Working));

        rig.Engine.Push(ListRig.Empty(new AccountActivity(false, "Sign-in failed (network).", "browser")));

        rig.Model.Step.Should().Be(WelcomeStep.Connect);
        rig.Model.Accounts.SignInFailureDetail.Should().Be("Sign-in failed (network).");
        rig.Model.Accounts.ShowBrowserFallbackLink.Should().BeTrue();
        rig.Model.Accounts.DeviceRow.IsOn.Should().BeTrue();
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
    public async Task Continue_with_the_switch_off_and_a_google_account_makes_no_broker_call()
    {
        var rig = Create();
        await Start(rig);
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));
        await rig.Model.Accounts.DeviceRow.SetOnAsync(false);
        rig.Engine.Sent.Clear();

        await Press(rig.Model.ContinueCommand);

        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
        rig.Intent.Armed.Should().BeEmpty();
        rig.Source.Calls.Should().Equal("listing");
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
    public async Task Plus_then_Continue_with_the_switch_on_still_asks_windows()
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
    public async Task No_broker_call_other_than_the_listing_happens_before_Continue()
    {
        var rig = Create();
        await Start(rig);
        await rig.Model.Accounts.DeviceRow.SetOnAsync(false);
        await rig.Model.Accounts.DeviceRow.SetOnAsync(true);
        rig.Engine.Push(With(Idle, Account("g", "google", "you@example.com")));

        rig.Source.Calls.Should().Equal("listing");
        rig.Intent.Armed.Should().BeEmpty();
        rig.Engine.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_local_only_windows_account_hides_the_row_disables_Continue_and_Skip_closes()
    {
        var rig = Create(None);
        await Start(rig);
        var closed = 0;
        rig.Model.CloseRequested += (_, _) => closed++;

        rig.Model.Accounts.DeviceRow.IsVisible.Should().BeFalse();
        rig.Model.Accounts.ShowListEmptyText.Should().BeTrue();
        rig.Model.CanContinue.Should().BeFalse();

        await Press(rig.Model.SkipCommand);

        closed.Should().Be(1);
    }

    [Fact]
    public async Task A_conflicting_browser_account_enables_Continue_through_the_list_not_the_switch()
    {
        var rig = Create();
        await Start(rig);

        rig.Engine.Push(With(Idle, Account("b", "microsoft", "me@example.com")));

        rig.Model.Accounts.DeviceRow.IsOn.Should().BeFalse();
        rig.Model.CanContinue.Should().BeTrue("the browser account is in the list");
        await Press(rig.Model.ContinueCommand);
        rig.Model.Step.Should().Be(WelcomeStep.Connected);
        rig.Engine.Sent.Should().BeEmpty();
    }
}
