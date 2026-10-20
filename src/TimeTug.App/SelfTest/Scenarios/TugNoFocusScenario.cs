#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Platform.Capture;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Tug;
using WinRT.Interop;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-no-focus</c>: Windows did not hand over the keyboard, so the tug says "Click anywhere to respond"; the first
/// click on any part of the screen takes the keyboard, the banner goes, and the keys then work (Esc dismisses with one command).
/// The click is a real mouse press at a spot with no control on it, not a call into the window.
/// </summary>
internal static class TugNoFocusScenario
{
    public const string Name = "tug-no-focus";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, 1, cancellation);
        var run = rig.Run;

        run.Check(await rig.PresentAsync(rig.Request("e-focus", "Weekly sync", TimeSpan.FromSeconds(180))), "a takeover opens");
        var primary = rig.Host.Primary!;
        await rig.Settle();

        // The keyboard goes elsewhere: the tug has to say so and must not wait for keys that will never come.
        var lost = await rig.TakeKeyboardAwayAsync();
        run.Info("window activation: " + string.Join(" | ", primary.ActivationLog));
        if (!lost)
        {
            // This machine would not give the keyboard to another window; report the loss the way the window does.
            run.Info("the foreground could not be taken from the tug; the loss is reported to the controller directly");
            await rig.Controller.ReportFocusAsync(false);
            lost = await rig.WaitFor(() => primary.ViewModel.ShowClickToRespond);
        }

        run.Check(lost, "no keyboard shows Click anywhere to respond");
        await rig.Settle(600);
        run.Check(ScenarioUi.FindNamed<Border>(primary, "ClickBanner") is { Visibility: Visibility.Visible }, "the banner is visible");
        run.Check(rig.Controller.State == TugState.ShownWithoutFocus, "the controller records the missing keyboard: " + rig.Controller.State);
        run.Check(ScenarioUi.HasButton(primary, "Join meeting") && ScenarioUi.HasButton(primary, "Dismiss this reminder"), "Join and Dismiss still work by mouse");
        await run.ShotAsync(primary, "banner");
        await rig.HoldAsync();

        // The first click anywhere (top left corner, no control there) gives the keyboard back.
        rig.HideAnchor();
        var clicked = MouseInput.ClickAt(rig.Main.X + 60, rig.Main.Y + 60);
        run.Check(clicked, "a mouse click was delivered at the top left of the screen");
        var gotKeys = await rig.WaitFor(() => !primary.ViewModel.ShowClickToRespond, 4000);
        run.Info("window activation after the click: " + string.Join(" | ", primary.ActivationLog));
        run.Check(gotKeys, "the first click takes the keyboard and the banner goes");
        run.Check(TugWindowChrome.IsForeground(WindowNative.GetWindowHandle(primary)), "the tug is the window Windows sends keys to");
        run.Check(rig.Controller.State == TugState.Shown, "the controller records that the tug has the keyboard: " + rig.Controller.State);
        run.Check(rig.Terminal.Count == 0, "the click answered nothing");
        await run.ShotAsync(primary, "after-click");

        run.Check(await primary.ViewModel.HandleKeyAsync(TugKey.Escape), "Esc is routed to Dismiss");
        run.Check(await rig.WaitHiddenAsync(), "the windows close");
        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-focus"]), "the engine received exactly one command: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
