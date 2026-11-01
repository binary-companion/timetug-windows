#if SELFTEST
using TimeTug.Platform.Broker;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>brokered-unavailable</c>: Windows sign-in cannot be used here (the scripted broker answers that it is unavailable). The "Microsoft account on this
/// PC" card says "Windows sign-in isn't available here" and offers "Use your browser instead"; pressing that starts the browser sign-in for Microsoft.
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
        rig.Broker.Interactive = new BrokerOutcome.Unavailable(BrokerReason.BrokerUnavailable);
        run.Check(await rig.WaitFor(() => model.Providers[0].Name == "Use me@example.com"), "the card names the Windows account");
        run.Check(ScenarioUi.Invoke(window, "Connect Use me@example.com"), "the card is pressed");
        run.Check(await rig.WaitFor(() => model.Providers[0].OffersBrowserFallback), "the card offers the browser after the attempt failed");
        run.Check(model.Providers[0].Error == "Windows sign-in isn't available here", "and says why: " + model.Providers[0].Error);
        run.Check(model.Accounts.ErrorText is null, "the engine's own sentence is not repeated elsewhere");
        run.Check(model.Providers[0].BrowserFallbackText == "Use your browser instead", "the button reads \"Use your browser instead\"");
        run.Check(model.Providers.Skip(1).All(p => !p.OffersBrowserFallback), "no other card offers it");
        run.Check(model.Step == TimeTug.Presentation.Welcome.WelcomeStep.Connect && rig.Latest is { Accounts.Count: 0 }, "nothing was added and the window stays on step 1");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(window)));
        await rig.Settle(900);
        await run.ShotAsync(window, "unavailable");

        run.Check(rig.Host.BrowserSignInAttempts == 0, "the browser has not been asked for yet");
        run.Check(ScenarioUi.Invoke(window, "Use your browser to sign in to Microsoft instead"), "\"Use your browser instead\" is pressed");
        run.Check(await rig.WaitFor(() => rig.Host.BrowserSignInAttempts == 1), "the engine started the browser sign-in for Microsoft");
        run.Check(rig.Broker.Calls.Count(c => c.StartsWith("interactive", StringComparison.Ordinal)) == 1, "and did not ask the broker again: " + string.Join(", ", rig.Broker.Calls));

        return rig.Finish();
    }
}
#endif
