#if SELFTEST
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using TimeTug.App.Flyout;
using TimeTug.Platform.Flyout;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>flyout-empty</c>: a day with no meetings. The flyout opens next to the (stand-in) tray icon, sits on a monitor above
/// the icon, is a tool window that is not in the taskbar or Alt+Tab, shows the header, the empty state and the footer, has a disabled
/// Settings button named "Settings" and a name on every control, and closes on Esc, on losing the active state and on a second Toggle.
/// It also follows the Windows settings: reduced transparency and a contrast theme give the solid backdrop, and turned-off animations
/// remove the open animation. The engine is sent nothing.
/// </summary>
internal static class FlyoutEmptyScenario
{
    public const string Name = "flyout-empty";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = FlyoutRig.Create(options, cancellation);
        var run = rig.Run;
        rig.Tug.Engine.Push(rig.State([]));

        run.Check(!rig.Host.IsOpen, "the flyout starts closed");
        var window = await rig.OpenAsync();
        run.Check(window is not null && rig.Host.IsOpen, "Toggle opens the flyout");
        if (window is null)
        {
            return rig.Tug.Finish();
        }

        // ---- what it shows ------------------------------------------------------------------------------------------
        run.Check(window.ViewModel.Weekday == "Friday" && window.ViewModel.Summary == "Oct 16 · No meetings today", "the header says Friday and no meetings today: " + window.ViewModel.Summary);
        run.Check(window.EmptyStateShown, "the empty state is shown");
        run.Check(window.EventCards.Count == 0 && !window.NoMoreShown, "no cards and no banner");
        run.Check(window.ViewModel.Footer == "Tugs you 1 min before", "the footer says when the Tug fires: " + window.ViewModel.Footer);
        run.Check(window.SettingsEntry is { IsEnabled: true } && AutomationProperties.GetName(window.SettingsEntry) == "Settings", "Settings is present, enabled and named Settings");
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));

        // ---- where it is ----------------------------------------------------------------------------------------------
        var handle = window.Handle;
        var outer = FlyoutWindowChrome.OuterBounds(handle);
        var scale = FlyoutWindowChrome.DpiOf(handle) / 96.0;
        run.Info($"window {outer}, scale {scale}, anchor {rig.Anchor.Rect}");
        var main = rig.Monitors.Primary;
        var expected = FlyoutPlacement.Compute(rig.Anchor.Rect, window.MeasureContent(), rig.Monitors.Monitors);
        run.Check(outer is { } o && Close(o, expected, 3), $"the window is where the placement computed ({outer} vs {expected})");
        run.Check(outer is { } p && p.Bottom <= rig.Anchor.Rect.Y && p.X >= main.Work.X && p.Right <= main.Work.Right && p.Y >= main.Work.Y, "it sits above the icon, inside the work area");
        run.Check(outer is { } w && Math.Abs(w.Width - Math.Round(360 * scale)) <= 2, "it is 360 DIP wide at the monitor's scale");
        run.Check(!window.AppWindow.IsShownInSwitchers && FlyoutWindowChrome.IsToolWindow(handle), "it is not in the taskbar or Alt+Tab");
        run.Check(window.Animated, "the open animation runs when Windows has animations on");
        run.Check(window.Backdrop == (DesktopAcrylicController.IsSupported() ? FlyoutBackdrop.Material : FlyoutBackdrop.Solid), "acrylic backdrop when Windows allows it: " + window.Backdrop);
        await rig.Tug.Settle(500);
        run.Info("activation: " + string.Join(" | ", window.ActivationLog));
        run.Check(rig.Host.IsOpen, "the flyout is still open half a second after it opened");
        if (!rig.Host.IsOpen)
        {
            return rig.Tug.Finish();
        }

        try
        {
            await run.ShotAsync(window, "empty");
        }
        catch (System.Runtime.InteropServices.COMException e)
        {
            run.Check(false, "the window closed under the screenshot: " + e.Message + " | activation: " + string.Join(" | ", window.ActivationLog) + " | " + string.Join("; ", rig.Tug.Diagnostics.Entries.Select(d => d.Message)));
            return rig.Tug.Finish();
        }

        await rig.Tug.HoldAsync();

        // ---- closing ----------------------------------------------------------------------------------------------------
        window.PressEscape();
        run.Check(!rig.Host.IsOpen, "Esc closes the flyout");
        await Task.Delay(500, cancellation);

        window = await rig.OpenAsync();
        run.Check(window is not null, "it opens again");
        window?.LoseActivation();
        run.Check(!rig.Host.IsOpen, "losing the active state closes it");
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);

        window = await rig.OpenAsync();
        run.Check(window is not null && rig.Host.Toggle() == false && !rig.Host.IsOpen, "a second Toggle closes it");
        run.Check(rig.Factory.Last is { Window: not null }, "each opening makes a window");
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);

        // ---- Windows settings --------------------------------------------------------------------------------------------
        rig.Tug.Settings.AnimationsEnabled = false;
        window = await rig.OpenAsync();
        run.Check(window is { Animated: false }, "with animations off the window opens without the open animation");
        rig.Host.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);
        rig.Tug.Settings.AnimationsEnabled = true;

        rig.Tug.Settings.TransparencyEffectsEnabled = false;
        window = await rig.OpenAsync();
        run.Check(window is { Backdrop: FlyoutBackdrop.Solid }, "with transparency off the backdrop is solid");
        run.Info("activation of the solid window: " + string.Join(" | ", window?.ActivationLog ?? []));
        await rig.Tug.Settle(400);
        if (window is not null)
        {
            await run.ShotAsync(window, "solid");
        }

        rig.Tug.Settings.TransparencyEffectsEnabled = true;
        rig.Tug.Settings.HighContrast = true;
        rig.Tug.Settings.Raise();
        await rig.Tug.Settle(500);
        run.Info("activation at the contrast step: " + string.Join(" | ", rig.Factory.Last?.Window?.ActivationLog ?? []));
        run.Check(rig.Window is { Backdrop: FlyoutBackdrop.Solid }, "a contrast theme switches the open window to the solid backdrop");
        if (rig.Window is { } contrast)
        {
            await run.ShotAsync(contrast, "contrast");
        }

        foreach (var entry in rig.Tug.Diagnostics.Entries)
        {
            run.Info($"{entry.Level} {entry.Category}: {entry.Message}");
        }

        rig.Host.Close();
        run.Check(rig.Tug.Terminal.Count == 0 && !rig.Tug.Engine.Sent.Any(c => !c.StartsWith("presented:", StringComparison.Ordinal)), "the flyout sent the engine nothing: " + string.Join(", ", rig.Tug.Engine.Sent));
        return rig.Tug.Finish();
    }

    private static bool Close(PixelRect a, PixelRect b, int tolerance) =>
        Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance && Math.Abs(a.Width - b.Width) <= tolerance && Math.Abs(a.Height - b.Height) <= tolerance;
}
#endif
