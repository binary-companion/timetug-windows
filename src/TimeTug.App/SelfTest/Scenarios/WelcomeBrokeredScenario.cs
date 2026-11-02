#if SELFTEST
using Microsoft.UI.Xaml;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>welcome-brokered</c>: a first run over the real engine with a scripted Windows account broker and recorded Graph answers. The welcome window
/// opens by itself on "Welcome to TimeTug"; Get started shows "Connect your calendars": an empty box with no pinned row and no switch, and nothing but the
/// listing has been asked of Windows. Continue asks for the Windows dialog. Closing it leaves the window on step 2 with no error and "Use your browser instead";
/// allowing it adds the Windows account as an ordinary Microsoft row (with the Microsoft logo) and moves to step 3. A second pass shows that an account added with
/// "+" first lets Continue go on with no Windows dialog. Controls are pressed through UI Automation, so their names are exercised too.
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

        // ---- step 2: an empty box, no pinned row ----------------------------------------------------------------------------------------
        run.Check(ScenarioUi.Invoke(window, "Get started"), "Get started is pressed");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connect), "the window shows \"Connect your calendars\"");
        run.Check(await rig.WaitFor(() => model.Accounts.WindowsAccountOffered), "the Windows account is offered: Windows listed one");
        run.Check(model.CanContinue && window.Continue.IsEnabled, "Continue is enabled");
        run.Check(box.EmptyLineText.Visibility == Visibility.Visible, "the box says it is empty: nothing is added until Continue");
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
        run.Check(model.CanContinue && window.Continue.IsEnabled, "Continue can be pressed again");
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
        run.Check(model.Accounts.ListAccounts is [{ Mark: TimeTug.Presentation.Accounts.ProviderMark.Microsoft, LogoAutomationName: "Microsoft logo", AutomationName: "Microsoft, me@example.com, Connected" }],
            "it is an ordinary Microsoft row with the Microsoft logo and no tag: " + string.Join(" | ", model.Accounts.ListAccounts.Select(a => a.AutomationName)));
        run.Check(await rig.WaitFor(() => model.Accounts.NextMeetingText.Contains("Design review", StringComparison.Ordinal) && model.Accounts.NextMeetingText.Contains("2:30", StringComparison.Ordinal)),
            "and the next meeting: " + model.Accounts.NextMeetingText);
        run.Check(rig.Latest is { Accounts: [{ KindId: "microsoft-brokered", Status: "ok" }] }, "the engine holds one brokered account that is connected");
        run.Check(rig.Host.Secrets.Calls.Count == 0, "no credential was stored for it: " + string.Join(", ", rig.Host.Secrets.Calls));
        run.Check(rig.Broker.Calls.Count(c => c == "interactive:WindowsAccount") == 2, "the broker saw exactly the two dialogs: " + string.Join(", ", rig.Broker.Calls));
        await rig.Settle(900);
        await run.ShotAsync(window, "connected");

        // ---- a second first run: an account added with + first, then Continue asks Windows for nothing ------------------------------------
        var second = await SecondPassAsync(rig, window, cancellation);
        run.Check(second, "(second pass) Continue with the Windows account already added goes on without a Windows dialog");
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
        run.Check(await rig.WaitFor(() => model.Accounts.WindowsAccountOffered), "(second pass) the Windows account is offered again (removing it from the engine is not a dismissal)");
        run.Check(await rig.WaitFor(() => window.Continue.IsEnabled), "(second pass) Continue is enabled");
        await run.ShotAsync(window, "second-connect");

        // An account is added with + (the Microsoft picker, answered by the scripted broker): it is the Windows account, so nothing is offered any more.
        run.Check(await AccountsListUi.ChooseAsync((c, ms) => rig.WaitFor(c, ms), box, "Connect Microsoft"), "(second pass) Microsoft is chosen from the + menu");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:PickAccount")), "(second pass) the broker was asked for the account picker: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => rig.Latest is { Accounts.Count: 1 }), "(second pass) the account is added");
        run.Check(await rig.WaitFor(() => model.Accounts.ListAccounts is [{ StatusText: "Connected" }] && !model.Accounts.WindowsAccountOffered), "(second pass) the row shows it connected and nothing is offered");
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
