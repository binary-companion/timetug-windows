#if SELFTEST
using TimeTug.Presentation.Engine;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>flyout-tug-wins</c>: the flyout is open when a takeover fires. The flyout closes, the tug shows on the monitor, and
/// the flyout cannot be opened over it. Dismissing the tug sends exactly one terminal command and the flyout then opens again.
/// The flyout itself sends the engine nothing.
/// </summary>
internal static class FlyoutTugWinsScenario
{
    public const string Name = "flyout-tug-wins";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = FlyoutRig.Create(options, cancellation);
        var run = rig.Run;
        var tug = rig.Tug;
        tug.Engine.Push(rig.State(rig.Day()));

        var window = await rig.OpenAsync();
        run.Check(window is not null && rig.Host.IsOpen, "the flyout is open");
        run.Check(!rig.Presence.IsTugShowing, "no tug is showing yet");

        run.Check(await tug.PresentAsync(tug.Request("e-wins", "Planning", TimeSpan.FromSeconds(180))), "the takeover fires and the tug shows");
        run.Check(await tug.WaitFor(() => !rig.Host.IsOpen), "the flyout closed when the tug appeared");
        run.Check(rig.Presence.IsTugShowing, "the tug is reported as showing");
        run.Check(rig.Host.Toggle() == false && !rig.Host.IsOpen, "the flyout cannot be opened over the tug");
        run.Check(tug.Host.Primary is not null && tug.Host.WindowCount == 1, "the tug is still the only window up");
        await tug.Settle(800);
        await run.ShotAsync(tug.Host.Primary!, "tug");
        await tug.HoldAsync();
        run.Check(tug.Terminal.Count == 0, "nothing was sent to the engine yet: " + string.Join(", ", tug.Terminal));

        run.Check(ScenarioUi.Invoke(tug.Host.Primary!, "Dismiss this reminder"), "Dismiss is pressed");
        run.Check(await tug.WaitHiddenAsync(), "the tug closes");
        run.Check(tug.Terminal.SequenceEqual(["dismiss:e-wins"]), "exactly one terminal command went to the engine: " + string.Join(", ", tug.Terminal));
        run.Check(!rig.Presence.IsTugShowing, "the tug is gone");
        run.Check(rig.Host.Toggle() && rig.Host.IsOpen, "the flyout opens again once the tug is gone");
        rig.Host.Close();
        return tug.Finish();
    }
}
#endif
