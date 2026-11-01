#if SELFTEST
using Microsoft.UI.Xaml;
using TimeTug.App.Settings;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>accounts-brokered</c>: the real Settings window on the Accounts pane over the real engine, a scripted Windows account broker and recorded Graph
/// answers. Add an Account lists "Microsoft account on this PC", "Another Microsoft account" and "Microsoft (browser)"; adding through the picker reaches the
/// broker with the picker intent; the connected account carries the "On this PC" tag and the pane says how to withdraw the consent; removing the account
/// asks nothing of the broker.
/// </summary>
internal static class AccountsBrokeredScenario
{
    public const string Name = "accounts-brokered";

    private static readonly string[] CardIds = ["microsoft-brokered", "microsoft-brokered:pick", "microsoft"];

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartBrokeredAsync(options, cancellation);
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        var run = rig.Run;
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the first-run window opens (and is closed here: this scenario is about Settings)");
        rig.Shell.Welcome?.Close();

        var window = await rig.OpenSettingsAsync(SettingsPane.Accounts);
        run.Check(window is not null, "the Settings window opens on Accounts");
        if (window?.CurrentPage is not AccountsPage page)
        {
            return rig.Finish();
        }

        // ---- Add an Account --------------------------------------------------------------------------------------------------------------
        var cards = CardIds.Select(page.ProviderCardOf).ToArray();
        run.Check(cards.All(c => c is not null), "the three Microsoft cards are listed");
        if (cards.Any(c => c is null))
        {
            return rig.Finish();
        }

        run.Check(await rig.WaitFor(() => cards[0]!.Item.Name == "Use me@example.com"), "the first names the Windows account: " + cards[0]!.Item.Name);
        run.Check(cards[1]!.Item.Name == "Another Microsoft account" && cards[2]!.Item.Name == "Microsoft (browser)", "then \"Another Microsoft account\" and \"Microsoft (browser)\"");
        run.Check(cards.Take(2).All(c => c!.Item.Hint.StartsWith("Windows will ask you", StringComparison.Ordinal)) && cards[2]!.Item.Hint == "Opens your browser to sign in", "each says what it will do");
        run.Check(cards.All(c => c!.Add.IsEnabled), "all three can be pressed");
        run.Check(page.WithdrawConsent.Visibility == Visibility.Collapsed, "the withdraw-consent note is hidden while no Windows account is connected");
        await rig.Run.ShotAsync(window, "add-an-account");

        // ---- through the picker ---------------------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(cards[1]!.Add), "\"Another Microsoft account\" is pressed");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:PickAccount")), "the broker was asked for the account picker, not the Windows account: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => page.AccountCards.Count == 1), "the account is listed");
        if (page.AccountCards.Count == 0)
        {
            return rig.Finish();
        }

        var card = page.AccountCards[0];
        run.Check(card.Account is { KindId: "microsoft-brokered", Name: "me@example.com", IsOnThisPc: true }, "it is the Windows-brokered account");
        run.Check(card.Tag is not null, "its card carries the \"On this PC\" tag");
        run.Check(await rig.WaitFor(() => page.AccountCards.Count == 1 && page.AccountCards[0].Account.StatusText == "Connected"), "the engine says it is connected");
        var connectedCard = page.AccountCards[0];
        run.Check(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(connectedCard.Card) == "Microsoft, me@example.com, Connected, On this PC", "its card reads as provider, name, status and tag: " + Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(connectedCard.Card));
        run.Check(connectedCard.SignInAgain is null, "a healthy account does not offer Sign in again");
        run.Check(page.WithdrawConsent.Visibility == Visibility.Visible && page.WithdrawConsent.Text.Contains("account.live.com/consent/Manage", StringComparison.Ordinal), "the pane says how to withdraw the consent: " + page.WithdrawConsent.Text);
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Run.ShotAsync(window, "connected");

        // ---- Remove asks nothing of the broker ----------------------------------------------------------------------------------------------------
        var before = rig.Broker.Calls.Count;
        run.Check(await SettingsAccountsScenario.OpenRemoveQuestionAsync(window, rig, "me@example.com"), "Remove asks first");
        run.Check(await SettingsAccountsScenario.PressDialogAsync(window, rig, "PrimaryButton"), "Remove confirms");
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 0 }), "the engine removed the account");
        run.Check(await rig.WaitFor(() => page.AccountCards.Count == 0 && page.WithdrawConsent.Visibility == Visibility.Collapsed), "the pane is empty again and the withdraw-consent note is gone");
        await rig.Settle(600);
        run.Check(rig.Broker.Calls.Count == before, "removing the account asked nothing of the broker: " + string.Join(", ", rig.Broker.Calls.Skip(before)));
        run.Check(rig.Host.Secrets.Calls.Count == 0 || rig.Host.Secrets.Calls.All(c => c.StartsWith("remove ", StringComparison.Ordinal)), "and stored no credential");

        return rig.Finish();
    }
}
#endif
