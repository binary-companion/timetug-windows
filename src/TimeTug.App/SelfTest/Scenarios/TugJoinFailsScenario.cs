#if SELFTEST
using Microsoft.UI.Xaml.Controls;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-join-fails</c>: Windows cannot open the meeting link, so the tug stays up and shows the link in a box with a Copy
/// button; the engine is not told the person joined. Dismiss then ends the request with exactly one command. The meeting has
/// already started, so the countdown reads as late.
/// </summary>
internal static class TugJoinFailsScenario
{
    public const string Name = "tug-join-fails";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;
        rig.Launcher.Succeeds = false;

        run.Check(await rig.PresentAsync(rig.Request("e-late", "Customer call", TimeSpan.FromSeconds(-190))), "a meeting that already started opens");
        var primary = rig.Host.Primary!;
        run.Check(primary.ViewModel.CountdownValue == "Started 3 min ago", "a late tug says how late it is: " + primary.ViewModel.CountdownValue);
        run.Check(ScenarioUi.Invoke(primary, "Join meeting"), "Join is clicked");
        run.Check(await rig.WaitFor(() => rig.Host.Primary?.ViewModel.HasJoinFailed == true), "a failed Join shows the link");
        await rig.Settle();
        run.Check(rig.Launcher.Opened.SequenceEqual([TugRig.Link]), "Windows was asked to open the link once");
        run.Check(rig.Host.WindowCount == 2, "the request stays up on every monitor");
        run.Check(rig.Terminal.Count == 0, "the engine was not told to join: " + string.Join(", ", rig.Terminal));
        run.Check(ScenarioUi.FindNamed<TextBox>(rig.Host.Primary!, "JoinLinkBox") is { Text: TugRig.Link }, "the link is in a box to copy");
        run.Check(ScenarioUi.HasButton(rig.Host.Primary!, "Copy the meeting link"), "the Copy button is there");
        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Copy the meeting link"), "the Copy button can be invoked");
        await rig.Settle(600);
        await run.ShotAsync(rig.Host.Primary!, "link-to-copy");
        await rig.HoldAsync();

        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Dismiss this reminder"), "Dismiss can be pressed");
        run.Check(await rig.WaitHiddenAsync(), "Dismiss closes the windows");
        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-late"]), "Dismiss sent exactly one command: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
