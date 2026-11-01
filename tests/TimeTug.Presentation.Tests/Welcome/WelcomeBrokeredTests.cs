using System.Globalization;
using TimeTug.Presentation.Accounts;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tests.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.Presentation.Tests.Welcome;

/// <summary>The first-run window with "Microsoft account on this PC": the pre-selected card, naming the Windows account, declining, and the second step.</summary>
public class WelcomeBrokeredTests
{
    private static readonly AccountActivity Working = new(true, null, "browser");
    private static readonly AccountActivity Idle = new(false, null, "browser");

    private static (FakeEngineClient Engine, WelcomeViewModel Model, AccountsBrokeredTests.FakeIntent Intent) Create(AppState? first = null)
    {
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        var engine = new FakeEngineClient();
        var model = new WelcomeViewModel(engine, new ImmediateDispatcher(), new ReswLocalizer(), new Microsoft.Extensions.Time.Testing.FakeTimeProvider(States.Noon));
        var intent = new AccountsBrokeredTests.FakeIntent(engine.Sent);
        model.Accounts.BrokerIntents = intent;
        engine.Push(first ?? States.Empty(AccountsBrokeredTests.KindsWithBrokered));
        return (engine, model, intent);
    }

    [Fact]
    public void The_brokered_card_is_pre_selected_when_the_engine_lists_it_as_configured()
    {
        var (_, model, _) = Create();

        model.Providers.Select(p => p.IsPreselected).Should().Equal(true, false, false, false, false, false);
        model.Providers[0].Id.Should().Be("microsoft-brokered");
    }

    [Fact]
    public void Nothing_is_pre_selected_without_a_configured_brokered_kind()
    {
        var (_, plain, _) = Create(States.Empty());
        plain.Providers.Should().OnlyContain(p => !p.IsPreselected);

        var kinds = AccountsBrokeredTests.KindsWithBrokered.Select(k => k.Id == "microsoft-brokered" ? k with { IsConfigured = false } : k).ToList();
        var (_, unconfigured, _) = Create(States.Empty(kinds));
        unconfigured.Providers.Should().OnlyContain(p => !p.IsPreselected);
    }

    [Fact]
    public void Welcome_without_the_brokered_kind_is_unchanged()
    {
        var (_, model, _) = Create(States.Empty());

        model.Providers.Select(p => p.Name).Should().Equal("Google", "Microsoft", "iCloud", "Other (CalDAV)");
        model.Providers.Select(p => p.Id).Should().Equal("google", "microsoft", "icloud", "caldav");
        model.Step.Should().Be(WelcomeStep.Connect);
    }

    [Fact]
    public async Task The_card_names_the_windows_account_when_the_broker_lists_one_and_falls_back_otherwise()
    {
        var (_, named, _) = Create();
        named.Accounts.WindowsAccounts = new AccountsBrokeredTests.FakeWindowsAccounts("me@example.com");
        await named.Accounts.RefreshWindowsAccountAsync();
        named.Providers[0].Name.Should().Be("Use me@example.com");

        var (_, plain, _) = Create();
        plain.Accounts.WindowsAccounts = new AccountsBrokeredTests.FakeWindowsAccounts(null);
        await plain.Accounts.RefreshWindowsAccountAsync();
        plain.Providers[0].Name.Should().Be("Microsoft account on this PC");
    }

    [Fact]
    public async Task One_click_asks_the_engine_and_a_new_account_moves_to_step_two_with_the_count_and_the_next_meeting()
    {
        var (engine, model, _) = Create();

        await model.ConnectAsync("microsoft-brokered");
        engine.Push(States.Empty(AccountsBrokeredTests.KindsWithBrokered, Working));
        model.Step.Should().Be(WelcomeStep.Connect);

        var next = new NextMeetingInfo("Design review", States.Noon.AddMinutes(150), true);
        engine.Push(States.With([new AccountInfo("c1", "microsoft-brokered", "me@example.com", "ok", 3)], next, Idle, AccountsBrokeredTests.KindsWithBrokered));

        engine.Sent.Should().Equal("add:microsoft-brokered");
        model.Step.Should().Be(WelcomeStep.Connected);
        model.Accounts.Accounts.Should().ContainSingle().Which.Summary.Should().Be("Microsoft · me@example.com · 3 calendars");
        model.Accounts.NextMeetingText.Should().Contain("Design review").And.Contain("2:30");
    }

    [Fact]
    public async Task Declining_the_windows_dialog_returns_to_step_one_silently()
    {
        var (engine, model, intent) = Create();
        await model.ConnectAsync("microsoft-brokered");
        engine.Push(States.Empty(AccountsBrokeredTests.KindsWithBrokered, Working));

        engine.Push(States.Empty(AccountsBrokeredTests.KindsWithBrokered, Idle));

        model.Step.Should().Be(WelcomeStep.Connect);
        model.Accounts.IsBusy.Should().BeFalse();
        model.Accounts.ErrorText.Should().BeNull();
        model.Providers.Should().OnlyContain(p => !p.HasError && p.IsEnabled);
        intent.Current.Should().Be(BrokerIntent.WindowsAccount);
    }

    [Fact]
    public void The_pre_click_text_is_the_hint_of_the_brokered_card()
    {
        var (_, model, _) = Create();

        model.Providers[0].Hint.Should().Be("Windows will ask you to let TimeTug read your calendars. TimeTug stores no password and no sign-in token.");
    }
}
