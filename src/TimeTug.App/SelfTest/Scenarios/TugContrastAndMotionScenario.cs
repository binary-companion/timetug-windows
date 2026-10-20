#if SELFTEST
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>tug-contrast-and-motion</c>: with transparency effects off the backdrop is solid navy; in a contrast theme every
/// colour is the system's and the picture is gone; with animations off the countdown does not pulse. Restyling while the tug is up
/// keeps the same request and does not announce it a second time.
/// </summary>
internal static class TugContrastAndMotionScenario
{
    public const string Name = "tug-contrast-and-motion";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, cancellation);
        var run = rig.Run;

        run.Check(await rig.PresentAsync(rig.Request("e-look", "Design review", TimeSpan.FromSeconds(25))), "a takeover opens");
        run.Check(rig.Host.Primary!.ViewModel.Backdrop == TugBackdropKind.Acrylic, "the backdrop is acrylic by default");
        run.Check(rig.Host.Primary.ViewModel.PulseEnabled, "the countdown pulses by default (it is close)");
        await rig.Settle();
        await run.ShotAsync(rig.Host.Primary, "default");

        rig.Settings.TransparencyEffectsEnabled = false;
        rig.Settings.Raise();
        run.Check(await rig.WaitFor(() => rig.Host.Primary?.ViewModel.Backdrop == TugBackdropKind.SolidNavy && rig.Host.WindowCount == 2), "transparency off gives solid navy");
        await rig.Settle();
        await run.ShotAsync(rig.Host.Primary!, "transparency-off");

        rig.Settings.HighContrast = true;
        rig.Settings.Raise();
        run.Check(await rig.WaitFor(() => rig.Host.Primary?.ViewModel.Palette == TugPalette.SystemContrast && rig.Host.WindowCount == 2), "a contrast theme switches to the system colours");
        await rig.Settle();
        run.Check(rig.Host.Primary!.Announcements.Count == 0, "restyling does not announce the tug a second time");
        run.Check(ScenarioUi.Unnamed(rig.Host.Primary).Count == 0, "every control still has an automation name in the contrast look");
        await run.ShotAsync(rig.Host.Primary, "high-contrast");

        rig.Settings.HighContrast = false;
        rig.Settings.TransparencyEffectsEnabled = true;
        rig.Settings.AnimationsEnabled = false;
        rig.Settings.Raise();
        run.Check(await rig.WaitFor(() => rig.Host.Primary?.ViewModel.Palette == TugPalette.Brand && rig.Host.WindowCount == 2), "the brand palette returns");
        run.Check(rig.Host.Primary!.ViewModel.ReducedMotion && !rig.Host.Primary.ViewModel.PulseEnabled, "reduced motion turns the pulse off");
        await rig.Settle();
        await run.ShotAsync(rig.Host.Primary, "reduced-motion");

        run.Check(ScenarioUi.Invoke(rig.Host.Primary, "Dismiss this reminder"), "Dismiss can be pressed");
        run.Check(await rig.WaitHiddenAsync(), "the windows close");
        run.Check(rig.Terminal.SequenceEqual(["dismiss:e-look"]), "the restyles sent nothing; Dismiss sent exactly one command: " + string.Join(", ", rig.Terminal));
        return rig.Finish();
    }
}
#endif
