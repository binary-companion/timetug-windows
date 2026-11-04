#if SELFTEST
using Microsoft.UI.Xaml;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>welcome-local-only</c>: a first run on a PC whose Windows account is local-only (the scripted broker lists no account). Step 2 offers no
/// Windows account, shows the empty line, and Continue is disabled; "+" > Microsoft opens the Windows account picker, and Skip for now is the way
/// out. Nothing but the listing is asked of Windows until the person chooses Microsoft. A second run adds a Microsoft account with + and walks steps 3 (the calendars)
/// and 4 (start at sign-in, the taskbar card) to Done.
/// </summary>
internal static class WelcomeLocalOnlyScenario
{
    public const string Name = "welcome-local-only";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartBrokeredAsync(options, cancellation, listed: []);
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        var run = rig.Run;
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the welcome window opens by itself on a first run");
        if (rig.Shell.Welcome is not { } window)
        {
            return rig.Finish();
        }

        await SelfTestRunner.WaitForFrameAsync(window, cancellation);
        var model = window.ViewModel;
        var box = window.AccountsList;
        Task<bool> Wait(Func<bool> condition, int milliseconds) => rig.WaitFor(condition, milliseconds);

        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("list")), "Windows was asked which accounts it lists: " + string.Join(", ", rig.Broker.Calls));
        run.Check(ScenarioUi.Invoke(window, "Get started"), "Get started is pressed");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connect), "the window shows \"Connect your calendars\"");
        await rig.Settle(900);

        run.Check(!model.Accounts.WindowsAccountOffered, "a local-only Windows account has nothing to add: nothing is offered");
        run.Check(box.EmptyLineText.Visibility == Visibility.Visible && box.EmptyLineText.Text == "No accounts yet. Choose + to add one.", "the box says it is empty: " + box.EmptyLineText.Text);
        run.Check(!model.CanContinue && !window.Continue.IsEnabled, "Continue is disabled: there is nothing to continue to");
        run.Check(window.Continue.Visibility == Visibility.Visible && ScenarioUi.HasButton(window, "Skip for now"), "Skip for now is the way out");
        run.Check(rig.Broker.Calls.All(c => c == "list"), "nothing but the listing has gone to Windows: " + string.Join(", ", rig.Broker.Calls));
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await run.ShotAsync(window, "local-only");

        // "+" > Microsoft opens the Windows account picker (the person closes it here).
        rig.Broker.Interactive = new BrokerOutcome.Cancelled();
        var items = await AccountsListUi.OpenMenuAsync(Wait, box);
        run.Check(items.Count > 0 && items[0].Text == "Microsoft" && items[0].IsEnabled && items[0].KeyboardAcceleratorTextOverride.Length == 0, "the first entry is Microsoft, with no second line");
        await run.ShotAsync(window, "menu");
        await AccountsListUi.CloseMenuAsync(Wait, box);
        run.Check(await AccountsListUi.ChooseAsync(Wait, box, "Connect Microsoft"), "Microsoft is chosen");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:PickAccount")), "the broker was asked for the account picker: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => !model.Accounts.IsBusy), "closing the picker ends the attempt");
        await rig.Settle(600);
        run.Check(model.Step == WelcomeStep.Connect && rig.Latest is { Accounts.Count: 0 }, "the window stays on step 2 and nothing was added");
        run.Check(box.BrowserFallbackLink.Visibility == Visibility.Visible && box.ErrorLine.Text.Length == 0, "\"Use your browser instead\" is offered, with no error words");
        await run.ShotAsync(window, "picker-closed");

        // Skip for now closes the window.
        run.Check(ScenarioUi.Invoke(window, "Skip for now"), "Skip for now is pressed");
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is null), "the window closes");

        // A first run again: this time the person adds a Microsoft account with + > Microsoft (the picker is answered), goes on to the calendars and finishes.
        rig.Broker.Interactive = null;
        rig.Shell.ShowWelcome();
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the first-run window opens again");
        window = rig.Shell.Welcome!;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation);
        model = window.ViewModel;
        box = window.AccountsList;
        run.Check(ScenarioUi.Invoke(window, "Get started"), "Get started is pressed again");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connect), "the window shows \"Connect your calendars\" again");
        run.Check(await AccountsListUi.ChooseAsync(Wait, box, "Connect Microsoft"), "Microsoft is chosen from the + menu");
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 1 } && window.Continue.IsEnabled), "the account is added and Continue is enabled");
        run.Check(model.Step == WelcomeStep.Connect, "the window stays on step 2 until Continue");
        run.Check(ScenarioUi.Invoke(window, "Continue"), "Continue is pressed");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connected), "the window moves to step 3");
        run.Check(await WelcomeWalk.FromConnectedToDoneAsync(rig, window, "added"), "(added) steps 3 and 4 hold");
        return rig.Finish();
    }
}
#endif
