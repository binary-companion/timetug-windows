#if SELFTEST
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>welcome-brokered</c>: a first run over the real engine with a scripted Windows account broker and recorded Graph answers. The welcome window
/// opens by itself and lists "Microsoft account on this PC" first, pre-selected and named after the Windows account; pressing it asks the broker for the
/// Windows dialog. When the person closes that dialog nothing is added, nothing is said and the window stays on step 1; when they allow, step 2 shows the
/// account, its calendars and the next meeting. Controls are pressed through UI Automation, so their names are exercised too.
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

        // ---- step 1: the list ---------------------------------------------------------------------------------------------------------
        run.Check(model.Providers.Count > 0 && model.Providers[0].Id == "microsoft-brokered", "\"Microsoft account on this PC\" is listed first");
        run.Check(model.Providers.Select(p => p.Id).Take(2).SequenceEqual(["microsoft-brokered", "microsoft-brokered:pick"]), "and \"Another Microsoft account\" follows it");
        run.Check(model.Providers.Single(p => p.Id == "microsoft").Name == "Microsoft (browser)", "the browser sign-in is relabelled \"Microsoft (browser)\"");
        run.Check(model.Providers[0].IsPreselected && model.Providers.Skip(1).All(p => !p.IsPreselected), "the Windows account card is the pre-selected one");
        run.Check(await rig.WaitFor(() => model.Providers[0].Name == "Use me@example.com"), "the card names the account Windows is signed in with: " + model.Providers[0].Name);
        run.Check(model.Providers[0].Hint == "Windows will ask you to let TimeTug read your calendars. TimeTug stores no password and no sign-in token.", "the pre-click text is under the card");
        run.Check(model.Step == WelcomeStep.Connect, "the window starts on step 1");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Settle(900);
        await run.ShotAsync(window, "connect");

        // ---- the person closes the Windows dialog ---------------------------------------------------------------------------------------
        rig.Broker.Interactive = new BrokerOutcome.Cancelled();
        run.Check(ScenarioUi.Invoke(window, "Connect Use me@example.com"), "one press on the pre-selected card");
        run.Check(await rig.WaitFor(() => rig.Broker.Calls.Contains("interactive:WindowsAccount")), "the broker was asked for the Windows account dialog: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => !model.Accounts.IsBusy), "the attempt ends when the dialog is closed");
        await rig.Settle(600);
        run.Check(model.Step == WelcomeStep.Connect, "the window stays on step 1");
        run.Check(model.Accounts.ErrorText is null && model.Providers.All(p => !p.HasError && !p.OffersBrowserFallback), "nothing is said: no error and no extra button");
        run.Check(rig.Latest is { Accounts.Count: 0 }, "no account was added");
        run.Check(model.Providers.All(p => p.IsEnabled || !p.IsConfigured), "every card can be pressed again");
        await rig.Settle(900);
        await run.ShotAsync(window, "declined");

        // ---- the person allows ----------------------------------------------------------------------------------------------------------
        rig.Broker.Interactive = null;
        run.Check(ScenarioUi.Invoke(window, "Connect Use me@example.com"), "pressing it again");
        run.Check(await rig.WaitFor(() => model.Step == WelcomeStep.Connected), "the window moves to step 2");
        run.Check(await rig.WaitFor(() => model.Accounts.Accounts.Count == 1 && model.Accounts.Accounts[0].Summary.EndsWith("3 calendars", StringComparison.Ordinal)),
            "step 2 names the account and its calendars: " + string.Join(" | ", model.Accounts.Accounts.Select(a => a.Summary)));
        run.Check(model.Accounts.Accounts.Count == 1 && model.Accounts.Accounts[0].Summary == "Microsoft · me@example.com · 3 calendars", "\"Microsoft, 3 calendars\"");
        run.Check(await rig.WaitFor(() => model.Accounts.NextMeetingText.Contains("Design review", StringComparison.Ordinal) && model.Accounts.NextMeetingText.Contains("2:30", StringComparison.Ordinal)),
            "and the next meeting: " + model.Accounts.NextMeetingText);
        run.Check(rig.Latest is { Accounts: [{ KindId: "microsoft-brokered", Status: "ok" }] }, "the engine holds one brokered account that is connected");
        run.Check(rig.Host.Secrets.Calls.Count == 0, "no credential was stored for it: " + string.Join(", ", rig.Host.Secrets.Calls));
        run.Check(rig.Broker.Calls.Count(c => c == "interactive:WindowsAccount") == 2, "the broker saw exactly the two dialogs: " + string.Join(", ", rig.Broker.Calls));
        await rig.Settle(900);
        await run.ShotAsync(window, "connected");

        return rig.Finish();
    }
}
#endif
