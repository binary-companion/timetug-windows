#if SELFTEST
using Microsoft.UI.Xaml;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>brokered-unavailable</c>: Windows sign-in cannot be used here (the scripted broker answers that it is unavailable). Continue on the first-run window
/// leaves it on step 2 with "Windows sign-in isn't available here" under the box and the link "Use your browser instead"; pressing the link starts the browser
/// sign-in for Microsoft and does not ask the broker again.
/// </summary>
internal static class BrokeredUnavailableScenario
{
    public const string Name = "brokered-unavailable";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartBrokeredAsync(options, cancellation);
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        var run = rig.Run;
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is not null), "the welcome window opens");
        if (rig.Shell.Welcome is not { } window)
        {
            return rig.Finish();
        }

        await SelfTestRunner.WaitForFrameAsync(window, cancellation);
        var model = window.ViewModel;
        var box = window.AccountsList;
        rig.Broker.Interactive = new BrokerOutcome.Unavailable(BrokerReason.BrokerUnavailable);
        run.Check(ScenarioUi.Invoke(window, "Get started"), "Get started is pressed");
        run.Check(await rig.WaitFor(() => box.DeviceRowPanel.Visibility == Visibility.Visible && box.DeviceEmailText.Text == "me@example.com"), "the pinned row names the Windows account");
        run.Check(ScenarioUi.Invoke(window, "Continue"), "Continue is pressed");
        run.Check(await rig.WaitFor(() => box.BrowserFallbackLink.Visibility == Visibility.Visible), "the link is offered after the attempt failed");
        run.Check(box.ErrorLine.Text == "Windows sign-in isn't available here", "and the line says why: " + box.ErrorLine.Text);
        run.Check(box.ErrorDetailLine.Visibility == Visibility.Collapsed && model.Accounts.ErrorText is null, "the engine's own sentence is not repeated elsewhere");
        run.Check(box.BrowserFallbackLink.Content is "Use your browser instead", "the link reads \"Use your browser instead\"");
        run.Check(model.Step == WelcomeStep.Connect && rig.Latest is { Accounts.Count: 0 }, "nothing was added and the window stays on step 2");
        run.Check(box.DeviceToggle.IsOn, "the switch is still on");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Settle(900);
        await run.ShotAsync(window, "unavailable");

        run.Check(rig.Host.BrowserSignInAttempts == 0, "the browser has not been asked for yet");
        run.Check(ScenarioUi.Invoke(window, "Use your browser to sign in to Microsoft instead"), "\"Use your browser instead\" is pressed");
        run.Check(await rig.WaitFor(() => rig.Host.BrowserSignInAttempts == 1), "the engine started the browser sign-in for Microsoft");
        run.Check(rig.Broker.Calls.Count(c => c.StartsWith("interactive", StringComparison.Ordinal)) == 1, "and did not ask the broker again: " + string.Join(", ", rig.Broker.Calls));
        run.Check(await rig.WaitFor(() => box.BrowserFallbackLink.Visibility == Visibility.Collapsed), "the link goes away once a sign-in has started");

        return rig.Finish();
    }
}
#endif
