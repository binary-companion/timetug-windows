#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>welcome-brokered</c>: a first run over the real engine with a scripted Windows account broker and recorded Graph answers. The welcome window
/// opens by itself on "Welcome to TimeTug"; Get started shows "Connect your calendars" with the pinned row "This PC's Windows account" (the listed email, the
/// switch on) and nothing but the listing has been asked of Windows. Continue asks for the Windows dialog. Closing it leaves the window on step 2 with no error
/// and "Use your browser instead"; allowing it moves to step 3. A second pass shows that an account added with "+" and the switch off lets Continue go on with
/// no Windows dialog. Controls are pressed through UI Automation, so their names are exercised too.
/// </summary>
internal static class WelcomeBrokeredScenario
{
    public const string Name = "welcome-brokered";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartBrokeredAsync(options, cancellation);
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

        // ---- step 1: the listing runs while it is up ------------------------------------------------------------------------------------
        run.Check(model.Step == WelcomeStep.Intro, "the window starts on \"Welcome to TimeTug\"");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("list")), "Windows was asked which accounts it lists: " + string.Join(", ", rig.Broker.Calls));
        run.Check(rig.Broker.Calls.All(c => c == "list"), "and nothing else: no token, no dialog: " + string.Join(", ", rig.Broker.Calls));
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Settle(600);
        await run.ShotAsync(window, "intro");

        // ---- step 2: the pinned row -----------------------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(window, "Get started"), "Get started is pressed");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connect), "the window shows \"Connect your calendars\"");
        run.Check(await rig.WaitFor(() => box.DeviceRowPanel.Visibility == Visibility.Visible), "the pinned row is shown");
        run.Check(box.DeviceTitleText.Text == "This PC's Windows account" && box.DeviceEmailText.Text == "me@example.com", "it names \"This PC's Windows account\" and the listed email: " + box.DeviceEmailText.Text);
        run.Check(box.DeviceToggle.IsOn && box.DeviceToggle.IsEnabled, "its switch is on");
        run.Check(AutomationProperties.GetName(box.DeviceToggle) == "Use this PC's Windows account", "and named for a screen reader: " + AutomationProperties.GetName(box.DeviceToggle));
        run.Check(box.DeviceDescriptionText.Text == "Windows will ask you to let TimeTug read your calendars when you press Continue. TimeTug stores no password and no sign-in token.", "the pre-click line is under it");
        run.Check(model.CanContinue && window.Continue.IsEnabled, "Continue is enabled");
        run.Check(box.EmptyLineText.Visibility == Visibility.Visible, "the list under the pinned row says it is empty");
        run.Check(rig.Broker.Calls.All(c => c == "list"), "still nothing but the listing has gone to Windows: " + string.Join(", ", rig.Broker.Calls));
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Settle(900);
        await run.ShotAsync(window, "connect");

        // ---- Continue, and the person closes the Windows dialog ------------------------------------------------------------------------------
        rig.Broker.Interactive = new BrokerOutcome.Cancelled();
        run.Check(ScenarioUi.Invoke(window, "Continue"), "Continue is pressed");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:WindowsAccount")), "the broker was asked for the Windows account dialog: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => !model.Accounts.IsBusy), "the attempt ends when the dialog is closed");
        await rig.Settle(600);
        run.Check(model.Step == WelcomeStep.Connect, "the window stays on step 2");
        run.Check(model.Accounts.ErrorText is null && model.Accounts.SignInFailureText is null && box.ErrorLine.Text.Length == 0, "no error words");
        run.Check(box.DeviceToggle.IsOn, "the switch is still on");
        run.Check(box.BrowserFallbackLink.Visibility == Visibility.Visible && box.BrowserFallbackLink.Content is "Use your browser instead", "\"Use your browser instead\" is offered");
        run.Check(rig.Latest is { Accounts.Count: 0 }, "no account was added");
        await rig.Settle(900);
        await run.ShotAsync(window, "declined");

        // ---- Continue again, and the person allows -----------------------------------------------------------------------------------------
        rig.Broker.Interactive = null;
        run.Check(ScenarioUi.Invoke(window, "Continue"), "Continue is pressed again");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connected), "the window moves to step 3");
        run.Check(await rig.WaitFor(() => model.Accounts.Accounts.Count == 1 && model.Accounts.Accounts[0].Summary.EndsWith("3 calendars", StringComparison.Ordinal)),
            "step 3 names the account and its calendars: " + string.Join(" | ", model.Accounts.Accounts.Select(a => a.Summary)));
        run.Check(model.Accounts.Accounts.Count == 1 && model.Accounts.Accounts[0].Summary == "Microsoft · me@example.com · 3 calendars", "\"Microsoft, 3 calendars\"");
        run.Check(await rig.WaitFor(() => model.Accounts.NextMeetingText.Contains("Design review", StringComparison.Ordinal) && model.Accounts.NextMeetingText.Contains("2:30", StringComparison.Ordinal)),
            "and the next meeting: " + model.Accounts.NextMeetingText);
        run.Check(rig.Latest is { Accounts: [{ KindId: "microsoft-brokered", Status: "ok" }] }, "the engine holds one brokered account that is connected");
        run.Check(rig.Host.Secrets.Calls.Count == 0, "no credential was stored for it: " + string.Join(", ", rig.Host.Secrets.Calls));
        run.Check(rig.Broker.Calls.Count(c => c == "interactive:WindowsAccount") == 2, "the broker saw exactly the two dialogs: " + string.Join(", ", rig.Broker.Calls));
        await rig.Settle(900);
        await run.ShotAsync(window, "connected");

        // ---- a second first run: the switch off and an account added with +, then Continue asks Windows for nothing ----------------------------
        var second = await SecondPassAsync(rig, window, cancellation);
        run.Check(second, "(second pass) Continue with the switch off goes on without a Windows dialog");
        return rig.Finish();
    }

    // Removes the account and opens the first-run window again, the way the tray does when there is no account.
    private static async Task<bool> SecondPassAsync(EngineRig rig, TimeTug.App.Views.WelcomeWindow first, CancellationToken cancellation)
    {
        var run = rig.Run;
        var dialogsBefore = rig.Broker.Calls.Count(c => c.StartsWith("interactive", StringComparison.Ordinal));

        first.Close();
        await rig.Client.RemoveAccountAsync(rig.Latest!.Accounts[0].ConnectionId);
        if (!run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 0 }), "(second pass) the engine removed the account"))
        {
            return false;
        }

        rig.Shell.ShowWelcome();
        if (!run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "(second pass) the welcome window opens again"))
        {
            return false;
        }

        var window = rig.Shell.Welcome!;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation);
        var model = window.ViewModel;
        var box = window.AccountsList;
        run.Check(ScenarioUi.Invoke(window, "Get started"), "(second pass) Get started");
        run.Check(await rig.WaitFor(() => box.DeviceRowPanel.Visibility == Visibility.Visible), "(second pass) the pinned row is shown again");

        // The switch off: the person does not want the Windows account.
        run.Check(ScenarioUi.Toggle(box.DeviceToggle), "(second pass) the switch is turned off");
        run.Check(await rig.WaitFor(() => !box.DeviceToggle.IsOn), "(second pass) it stays off");
        run.Check(!model.CanContinue && !window.Continue.IsEnabled, "(second pass) with the switch off and an empty list Continue is disabled");
        await run.ShotAsync(window, "switch-off");

        // An account is added with + (the Microsoft picker, answered by the scripted broker): it is the Windows account, so the pinned row shows it connected.
        run.Check(await AccountsListUi.ChooseAsync((c, ms) => rig.WaitFor(c, ms), box, "Connect Microsoft"), "(second pass) Microsoft is chosen from the + menu");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:PickAccount")), "(second pass) the broker was asked for the account picker: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 1 }), "(second pass) the account is added");
        run.Check(await rig.WaitFor(() => model.Accounts.WindowsAccountConnected && box.DeviceStatusText.Text == "Connected"), "(second pass) the pinned row shows it connected: " + box.DeviceStatusText.Text);
        run.Check(model.Step == WelcomeStep.Connect, "(second pass) the window stays on step 2");
        var calls = rig.Broker.Calls.Count;

        run.Check(await rig.WaitFor(() => window.Continue.IsEnabled), "(second pass) Continue is enabled");
        run.Check(ScenarioUi.Invoke(window, "Continue"), "(second pass) Continue is pressed");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connected), "(second pass) the window moves to step 3");
        await rig.Settle(600);
        run.Check(rig.Broker.Calls.Count == calls, "(second pass) Continue asked Windows for nothing: " + string.Join(", ", rig.Broker.Calls.Skip(calls)));
        run.Check(rig.Broker.Calls.Count(c => c.StartsWith("interactive", StringComparison.Ordinal)) == dialogsBefore + 1, "(second pass) the only new dialog was the picker");
        await run.ShotAsync(window, "second-connected");
        return true;
    }
}
#endif
