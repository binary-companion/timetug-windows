#if SELFTEST
using Microsoft.UI.Xaml.Automation;
using Microsoft.Extensions.DependencyInjection;
using TimeTug.App.Settings;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>brokered-reconnect</c>: a connected Microsoft account on this PC whose Windows sign-in needs the person again (the clock passes the token's
/// expiry and the scripted broker answers that interaction is required). The account becomes "Sign in again", the flyout's problem row says "Open Accounts"
/// (the Windows dialog needs a stable window, so it is never opened from the flyout), Settings > Accounts offers Sign in again, and pressing it opens the
/// Windows dialog for that account and recovers the account without a restart.
/// </summary>
internal static class BrokeredReconnectScenario
{
    public const string Name = "brokered-reconnect";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartBrokeredAsync(options, cancellation);
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        var run = rig.Run;
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the first-run window opens");
        if (rig.Shell.Welcome is not { } welcome)
        {
            return rig.Finish();
        }

        await welcome.ViewModel.Accounts.ConnectBrokeredAsync(TimeTug.Presentation.Accounts.BrokerIntent.WindowsAccount);
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts: [{ KindId: "microsoft-brokered", Status: "ok", CalendarCount: 3 }] }), "the account is connected with its three calendars");
        welcome.Close();

        // ---- Windows needs the person again -------------------------------------------------------------------------------------------------
        rig.Broker.Silent = new BrokerOutcome.UiRequired(BrokerReason.SignedOut);
        var callsBefore = rig.Broker.Calls.Count;
        await rig.AdvanceAsync(TimeSpan.FromHours(2));
        await rig.RefreshAsync();
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts: [{ Status: "authExpired" }] }), "the account becomes \"Sign in again\" once its token cannot be renewed silently");
        run.Check(rig.Broker.Calls.Skip(callsBefore).Contains("silent"), "the engine asked the broker silently");
        run.Check(!rig.Broker.Calls.Skip(callsBefore).Any(c => c.StartsWith("interactive", StringComparison.Ordinal)), "and never opened a Windows dialog on its own");
        run.Check(rig.Latest is { Accounts.Count: 1, Events.Count: > 0 }, "the rest of the state is intact");

        // ---- the flyout: Open Accounts ---------------------------------------------------------------------------------------------------------
        var flyout = rig.Shell.Services.GetRequiredService<FlyoutViewModel>();
        run.Check(await rig.WaitFor(() => flyout.Problems.Count == 1), "the flyout lists the problem");
        var row = flyout.Problems.Count > 0 ? flyout.Problems[0] : null;
        run.Check(row is { ActionText: "Open Accounts", OpensAccounts: true }, "its row says \"Open Accounts\", not \"Sign in again\": " + row?.ActionText);

        // ---- Settings: Sign in again -----------------------------------------------------------------------------------------------------------
        var window = await rig.OpenSettingsAsync(SettingsPane.Accounts);
        run.Check(window is not null, "the Settings window opens on Accounts");
        if (window?.CurrentPage is not AccountsPage page)
        {
            return rig.Finish();
        }

        run.Check(await rig.WaitFor(() => page.AccountCards.Count == 1 && page.AccountCards[0].SignInAgain is not null), "the account offers Sign in again");
        run.Check(page.AccountCards[0].Account.StatusText == "Sign in again" && page.AccountCards[0].Account.IsOnThisPc, "and says so, with the \"On this PC\" tag");
        await rig.Run.ShotAsync(window, "sign-in-again");

        rig.Broker.Silent = null;
        rig.Broker.Interactive = null;
        run.Check(ScenarioUi.Invoke(page.AccountCards[0].SignInAgain!), "Sign in again is pressed");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:WindowsAccount")), "the Windows dialog was opened for the account: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts: [{ Status: "ok" }] }), "the account recovers without a restart");
        run.Check(await rig.WaitFor(() => page.AccountCards.Count == 1 && page.AccountCards[0].Account.StatusText == "Connected"), "and the pane says it is connected");
        run.Check(await rig.WaitFor(() => flyout.Problems.Count == 0), "the flyout's problem row is gone");
        await rig.Run.ShotAsync(window, "recovered");

        return rig.Finish();
    }
}
#endif
