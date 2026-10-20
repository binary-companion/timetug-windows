#if SELFTEST
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-join</c>: Join answers a takeover by opening the meeting link and telling the engine once, whether it is clicked
/// (with nothing focused) or chosen with Return. Each answer is exactly one terminal command.
/// </summary>
internal static class TugJoinScenario
{
    public const string Name = "tug-join";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;

        run.Check(await rig.PresentAsync(rig.Request("e-click", "Design review", TimeSpan.FromSeconds(50))), "a takeover with a link opens");
        await rig.Settle();
        await run.ShotAsync(rig.Host.Primary!, "before-join");
        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Join meeting"), "the Join button can be invoked");
        run.Check(await rig.WaitHiddenAsync(), "the windows close after Join");
        run.Check(rig.Launcher.Opened.SequenceEqual([TugRig.Link]), "Join opened the meeting link: " + string.Join(", ", rig.Launcher.Opened));
        run.Check(rig.Terminal.SequenceEqual(["join:e-click"]), "the engine received exactly one command: " + string.Join(", ", rig.Terminal));

        run.Check(await rig.PresentAsync(rig.Request("e-key", "Weekly sync", TimeSpan.FromSeconds(90))), "a second takeover opens");
        run.Check(rig.Host.Primary!.ViewModel.Hints.StartsWith("Return joins", StringComparison.Ordinal), "the hints say Return joins: " + rig.Host.Primary.ViewModel.Hints);
        run.Check(await rig.Host.Primary.ViewModel.HandleKeyAsync(TugKey.Return), "Return is routed to Join");
        run.Check(await rig.WaitHiddenAsync(), "the windows close after Return");
        run.Check(rig.Launcher.Opened.Count == 2, "the link was opened a second time");
        run.Check(rig.Terminal.SequenceEqual(["join:e-click", "join:e-key"]), "one more command, nothing repeated: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
