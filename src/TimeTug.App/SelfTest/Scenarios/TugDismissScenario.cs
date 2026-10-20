#if SELFTEST
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-dismiss</c>: Dismiss by the button, Esc, and Return on a meeting without a link each answer the request with
/// exactly one dismiss command; a meeting without a link has no Join button.
/// </summary>
internal static class TugDismissScenario
{
    public const string Name = "tug-dismiss";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;

        run.Check(await rig.PresentAsync(rig.Request("e-button", "Standup", TimeSpan.FromSeconds(30))), "a takeover opens");
        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Dismiss this reminder"), "the Dismiss button can be invoked");
        run.Check(await rig.WaitHiddenAsync(), "Dismiss closes the windows");

        run.Check(await rig.PresentAsync(rig.Request("e-esc", "Retro", TimeSpan.FromSeconds(60))), "a second takeover opens");
        run.Check(await rig.Host.Primary!.ViewModel.HandleKeyAsync(TugKey.Escape), "Esc is routed to Dismiss");
        run.Check(await rig.WaitHiddenAsync(), "Esc closes the windows");

        run.Check(await rig.PresentAsync(rig.Request("e-nolink", "Focus time", TimeSpan.FromSeconds(120), link: null, others: 0, calendar: null)), "a takeover without a link opens");
        run.Check(!ScenarioUi.HasButton(rig.Host.Primary!, "Join meeting"), "there is no Join button without a link");
        run.Check(rig.Host.Primary!.ViewModel.Hints == "Esc dismisses · 1, 5 or 0 snoozes", "the hints list only the keys that do something: " + rig.Host.Primary.ViewModel.Hints);
        await run.ShotAsync(rig.Host.Primary, "no-link");
        run.Check(await rig.Host.Primary.ViewModel.HandleKeyAsync(TugKey.Return), "Return dismisses when there is no link");
        run.Check(await rig.WaitHiddenAsync(), "Return closes the windows");

        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-button", "dismiss:e-esc", "dismiss:e-nolink"]), "each request received exactly one dismiss: " + string.Join(", ", rig.Terminal));
        run.Check(rig.Launcher.Opened.Count == 0, "no link was opened");
        return rig.Finish();
    }
}
#endif
