#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Settings;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>welcome-dismissed</c>: removing the Windows account is remembered, whichever way it is removed. First, on the first-run window: the Windows account's
/// row is selected and "-" drops it at once (no question, nothing asked of Windows), the box says it is empty, Continue is disabled and the removal is recorded.
/// Then, from a first run again, the account is added on Continue (Windows' dialog, allowed). In
/// Settings > Accounts the person selects its row, presses "-" and confirms; the flag is recorded once the engine says the account is gone. The first-run window
/// opened again then asks Windows nothing at all (no listing), offers nothing, and keeps Continue disabled with the empty box; "+" > Microsoft (the account picker)
/// adds the Windows account back and Continue then goes on without another Windows dialog.
/// </summary>
internal static class WelcomeDismissedScenario
{
    public const string Name = "welcome-dismissed";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartBrokeredAsync(options, cancellation);
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        var run = rig.Run;
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the welcome window opens by itself on a first run");
        if (rig.Shell.Welcome is not { } first)
        {
            return rig.Finish();
        }

        await SelfTestRunner.WaitForFrameAsync(first, cancellation);

        // ---- "-" on the Windows account's row in the first-run window -------------------------------------------------------------------------------------
        run.Check(!rig.Dismissals.IsDismissed, "nothing was removed yet: no dismissal is recorded");
        run.Check(ScenarioUi.Invoke(first, "Get started"), "Get started is pressed");
        run.Check(await rig.WaitFor(() => first.ViewModel.Step == WelcomeStep.Connect && first.ViewModel.Accounts.ListAccounts is [{ IsPending: true }]), "the Windows account is the box's only row");
        var firstBox = first.AccountsList;
        run.Check(AccountsListUi.Select(firstBox, AccountsViewModel.WindowsOfferRowId) && await rig.WaitFor(() => firstBox.RemoveMinus.IsEnabled, 3000), "the row is selected and - is enabled");
        run.Check(ScenarioUi.Invoke(firstBox.RemoveMinus), "- is pressed");
        run.Check(await rig.WaitFor(() => first.ViewModel.Accounts.ListAccounts.Count == 0, 3000), "the row is gone");
        await rig.Settle(600);
        run.Check(firstBox.OpenDialog is null && VisualTreeHelper.GetOpenPopupsForXamlRoot(first.Content.XamlRoot).Count == 0, "no question was asked: nothing was connected");
        run.Check(rig.Dismissals.IsDismissed, "the removal is remembered");
        run.Check(firstBox.EmptyLineText.Visibility == Visibility.Visible && !first.Continue.IsEnabled, "the box says it is empty and Continue is disabled");
        run.Check(rig.Latest is { Accounts.Count: 0 } && rig.Broker.Calls.All(c => c == "list"), "nothing but the listing went to Windows: " + string.Join(", ", rig.Broker.Calls));
        await run.ShotAsync(first, "row-removed");
        first.Close();

        // ---- a first run again, and this time the account is added on Continue ---------------------------------------------------------------------------------
        rig.Dismissals.Clear();
        rig.Shell.ShowWelcome();
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the first-run window opens again after the removal was forgotten");
        first = rig.Shell.Welcome!;
        await SelfTestRunner.WaitForFrameAsync(first, cancellation);
        run.Check(ScenarioUi.Invoke(first, "Get started"), "Get started is pressed");
        run.Check(await rig.WaitFor(() => first.ViewModel.Step == WelcomeStep.Connect && first.ViewModel.CanContinue), "Continue is enabled: Windows lists an account");
        run.Check(ScenarioUi.Invoke(first, "Continue"), "Continue is pressed");
        run.Check(await rig.WaitFor(() => first.ViewModel.Step == WelcomeStep.Connected), "the Windows dialog is allowed and the window moves to step 3");
        run.Check(rig.Latest is { Accounts: [{ KindId: "microsoft-brokered", Status: "ok" }] }, "the Windows account is connected");
        first.Close();

        // ---- Settings: remove it with - ------------------------------------------------------------------------------------------------------------------
        var settings = await rig.OpenSettingsAsync(SettingsPane.Accounts);
        run.Check(settings is not null, "the Settings window opens on Accounts");
        if (settings?.CurrentPage is not AccountsPage page)
        {
            return rig.Finish();
        }

        var box = page.AccountsList;
        Task<bool> Wait(Func<bool> condition, int milliseconds) => rig.WaitFor(condition, milliseconds);
        run.Check(await Wait(() => box.Accounts!.ListAccounts.Count == 1, 5000), "the Windows account is an ordinary row");
        var id = box.Accounts!.ListAccounts[0].ConnectionId;
        run.Check(AccountsListUi.Select(box, id) && await Wait(() => box.RemoveMinus.IsEnabled, 3000), "the row is selected and - is enabled");
        run.Check(ScenarioUi.Invoke(box.RemoveMinus) && await Wait(() => box.OpenDialog is not null, 3000), "- asks the question");
        run.Check(!rig.Dismissals.IsDismissed, "asking records nothing");
        run.Check(await AccountsListUi.PressDialogAsync(Wait, settings.RootElement.XamlRoot, "PrimaryButton"), "Remove confirms");
        run.Check(await Wait(() => rig.Latest is { Accounts.Count: 0 }, 5000), "the engine removed the account");
        run.Check(await Wait(() => rig.Dismissals.IsDismissed, 3000), "the removal is remembered");
        settings.Close();

        // ---- the first-run window again: nothing is asked of Windows -----------------------------------------------------------------------------------
        var listingsBefore = rig.Broker.Calls.Count(c => c == "list");
        var dialogsBefore = rig.Broker.Calls.Count(c => c.StartsWith("interactive", StringComparison.Ordinal));
        rig.Shell.ShowWelcome();
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the first-run window opens again");
        var window = rig.Shell.Welcome!;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation);
        var model = window.ViewModel;
        var again = window.AccountsList;
        run.Check(ScenarioUi.Invoke(window, "Get started"), "Get started is pressed");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connect), "the window shows \"Connect your calendars\"");
        await rig.Settle(900);
        run.Check(rig.Broker.Calls.Count(c => c == "list") == listingsBefore, "Windows was not even asked which accounts it lists: " + string.Join(", ", rig.Broker.Calls));
        run.Check(!model.Accounts.WindowsAccountOffered && !model.Accounts.ListingPending, "nothing is offered");
        run.Check(!model.CanContinue && !window.Continue.IsEnabled, "Continue is disabled with the empty box");
        run.Check(again.EmptyLineText.Visibility == Visibility.Visible, "the box says it is empty");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await run.ShotAsync(window, "dismissed");

        // ---- + > Microsoft adds it back -----------------------------------------------------------------------------------------------------------------
        run.Check(await AccountsListUi.ChooseAsync(Wait, again, "Connect Microsoft"), "Microsoft is chosen from the + menu");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:PickAccount")), "the account picker opens: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => model.Accounts.ListAccounts is [{ Name: "me@example.com" }]), "the Windows account is back as a row");
        run.Check(model.Step == WelcomeStep.Connect, "the window stays on step 2");
        var calls = rig.Broker.Calls.Count;
        run.Check(await rig.WaitFor(() => window.Continue.IsEnabled), "Continue is enabled");
        run.Check(ScenarioUi.Invoke(window, "Continue"), "Continue is pressed");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connected), "the window moves to step 3");
        run.Check(rig.Broker.Calls.Count == calls && rig.Broker.Calls.Count(c => c.StartsWith("interactive", StringComparison.Ordinal)) == dialogsBefore + 1, "Continue asked Windows for nothing; the picker was the only new dialog");
        await run.ShotAsync(window, "re-added");

        return rig.Finish();
    }
}
#endif
