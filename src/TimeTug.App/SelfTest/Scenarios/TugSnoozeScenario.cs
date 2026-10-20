#if SELFTEST
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-snooze</c>: the Snooze menu offers only the engine's options; pressing 5 snoozes for five minutes and sends
/// exactly one snooze command; a key whose option the engine did not offer does nothing.
/// </summary>
internal static class TugSnoozeScenario
{
    public const string Name = "tug-snooze";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;

        var request = rig.Request("e-snooze", "Quarterly planning", TimeSpan.FromSeconds(245)) with { SnoozeOptions = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300)] };
        run.Check(await rig.PresentAsync(request), "a takeover opens");
        var model = rig.Host.Primary!.ViewModel;
        run.Check(model.SnoozeChoices.Select(c => c.Title).SequenceEqual(["1 minute", "5 minutes"]), "the menu offers what the engine offered: " + string.Join(", ", model.SnoozeChoices.Select(c => c.Title)));
        run.Check(ScenarioUi.HasButton(rig.Host.Primary, "Snooze this reminder"), "the Snooze button is there");
        run.Check(model.Hints.Contains("snooze", StringComparison.Ordinal), "the hints list the snooze keys: " + model.Hints);
        await run.ShotAsync(rig.Host.Primary, "snooze-offered");

        run.Check(!await model.HandleKeyAsync(TugKey.Digit0), "0 does nothing when 10 minutes was not offered");
        run.Check(rig.Terminal.Count == 0 && rig.Host.WindowCount == 2, "nothing was sent and the tug is still up");
        run.Check(await model.HandleKeyAsync(TugKey.Digit5), "key 5 is routed to Snooze");
        run.Check(await rig.WaitHiddenAsync(), "the windows close after the snooze");
        run.Check(rig.Terminal.SequenceEqual(["snooze:e-snooze:300"]), "the engine received exactly one snooze for 5 minutes: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
