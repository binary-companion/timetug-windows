#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using TimeTug.App.Settings;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>accounts-brokered</c>: the real Settings window on the Accounts pane over the real engine, a scripted Windows account broker and recorded Graph
/// answers. Windows lists one account (not the one that will be added), so the pinned "This PC's Windows account" row is shown with its switch off; the "+" menu
/// has a single "Microsoft" entry that runs the account picker; the connected account is a list row with the "On this PC" tag and the pane says how to withdraw the
/// consent; "-" asks the question with the withdrawal text, removing asks nothing of the broker, focus lands on "+" and the removal is announced.
/// </summary>
internal static class AccountsBrokeredScenario
{
    public const string Name = "accounts-brokered";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartBrokeredAsync(options, cancellation, listed: [new BrokerAccount(FakeBrokerClient.AccountId, "windows-user@example.test")]);
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

        var box = page.AccountsList;
        Task<bool> Wait(Func<bool> condition, int milliseconds) => rig.WaitFor(condition, milliseconds);

        // ---- the pinned row and the + menu ----------------------------------------------------------------------------------------------
        run.Check(await rig.WaitFor(() => box.DeviceRowPanel.Visibility == Visibility.Visible), "the pinned row is shown for the account Windows lists");
        run.Check(box.DeviceEmailText.Text == "windows-user@example.test" && !box.DeviceToggle.IsOn, "it names that account and its switch is off: nothing is connected");
        run.Check(box.DeviceDescriptionText.Text.StartsWith("Windows will ask you", StringComparison.Ordinal) && !box.DeviceDescriptionText.Text.Contains("Continue", StringComparison.Ordinal), "with what Windows will do, in Settings' words");
        var items = await AccountsListUi.OpenMenuAsync(Wait, box);
        run.Check(items.Select(i => i.Text).Take(2).SequenceEqual(["Microsoft", "Google"]), "the + menu starts with Microsoft and Google: " + string.Join(", ", items.Select(i => i.Text)));
        run.Check(items.Count(i => i.Text.StartsWith("Microsoft", StringComparison.Ordinal)) == 1 && items[0].KeyboardAcceleratorTextOverride.Length == 0, "there is one Microsoft entry and it has no second line");
        run.Check(items.Last().Text == "Other (CalDAV)", "Other (CalDAV) is last");
        await rig.Run.ShotAsync(window, "add-an-account");
        await AccountsListUi.CloseMenuAsync(Wait, box);
        run.Check(box.WithdrawConsent.Visibility == Visibility.Collapsed, "the withdraw-consent note is hidden while no Windows account is connected");

        // ---- through the picker ---------------------------------------------------------------------------------------------------------
        run.Check(await AccountsListUi.ChooseAsync(Wait, box, "Connect Microsoft"), "Microsoft is chosen");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:PickAccount")), "the broker was asked for the account picker, not the Windows account: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => page.AccountsList.Accounts!.ListAccounts.Count == 1 && page.AccountsList.Accounts.ListAccounts[0].StatusText == "Connected"), "the account is listed and connected");
        if (box.Accounts!.ListAccounts.Count == 0)
        {
            return rig.Finish();
        }

        var account = box.Accounts.ListAccounts[0];
        run.Check(account is { KindId: "microsoft-brokered", Name: "me@example.com", IsOnThisPc: true }, "it is the Windows-brokered account, a list row because it is not the account Windows lists");
        run.Check(!box.DeviceToggle.IsOn, "the pinned row's switch stays off: it is another account");
        run.Check(AccountsListUi.RowNames(box).SequenceEqual(["Microsoft, me@example.com, Connected, On this PC"]), "its row reads as provider, name, status and tag: " + string.Join(" | ", AccountsListUi.RowNames(box)));
        run.Check(AccountsListUi.Container(box, account.ConnectionId) is { } realized && realized.Visibility == Visibility.Visible && box.RowSignInAgain(account.ConnectionId) is null, "a healthy account does not offer Sign in again");
        run.Check(box.WithdrawConsent.Visibility == Visibility.Visible && box.WithdrawConsent.Text.Contains("account.live.com/consent/Manage", StringComparison.Ordinal), "the pane says how to withdraw the consent: " + box.WithdrawConsent.Text);
        run.Check(AccountsListUi.ExposesListPatterns(box), "the list and its row expose the Selection patterns" + ": " + AccountsListUi.ListPatternReport(box).Detail);
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Run.ShotAsync(window, "connected");

        // ---- "-" asks first, removing asks nothing of the broker ----------------------------------------------------------------------------
        run.Check(box.Accounts.SelectedAccountId is null && !box.RemoveMinus.IsEnabled, "nothing is selected at first and - is disabled");
        run.Check(AccountsListUi.Select(box, account.ConnectionId), "the row is selected through UI Automation");
        run.Check(await rig.WaitFor(() => box.RemoveMinus.IsEnabled && AutomationProperties.GetName(box.RemoveMinus) == "Remove me@example.com"), "- is enabled and named for the row: " + AutomationProperties.GetName(box.RemoveMinus));
        var before = rig.Broker.Calls.Count;
        run.Check(ScenarioUi.Invoke(box.RemoveMinus), "- is pressed");
        run.Check(await rig.WaitFor(() => box.OpenDialog is not null), "the question opens");
        run.Check(box.OpenDialog is { Title: "Remove me@example.com?" } dialog && dialog.Content is Microsoft.UI.Xaml.Controls.TextBlock { Text: var body }
            && body.StartsWith("Its calendars will no longer appear", StringComparison.Ordinal) && body.Contains("account.live.com/consent/Manage", StringComparison.Ordinal), "naming the account, what is lost and how to withdraw the consent");
        await rig.Settle(900);
        await rig.Run.ShotAsync(window, "remove-question");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, window.RootElement.XamlRoot, "PrimaryButton"), "Remove confirms");
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 0 }), "the engine removed the account");
        run.Check(await rig.WaitFor(() => box.Accounts.ListAccounts.Count == 0 && box.WithdrawConsent.Visibility == Visibility.Collapsed), "the list is empty again and the withdraw-consent note is gone");
        run.Check(box.EmptyLineText.Visibility == Visibility.Visible, "the empty line shows");
        run.Check(await rig.WaitFor(() => ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(window.RootElement.XamlRoot), box.AddPlus), 4000), "focus landed on + (no row is left): " + AccountsListUi.FocusedName(window.RootElement.XamlRoot));
        await rig.Settle(600);
        run.Check(rig.Broker.Calls.Count == before, "removing the account asked nothing of the broker: " + string.Join(", ", rig.Broker.Calls.Skip(before)));
        run.Check(rig.Host.Secrets.Calls.Count == 0 || rig.Host.Secrets.Calls.All(c => c.StartsWith("remove ", StringComparison.Ordinal)), "and stored no credential");
        await rig.Run.ShotAsync(window, "removed");

        return rig.Finish();
    }
}
#endif
