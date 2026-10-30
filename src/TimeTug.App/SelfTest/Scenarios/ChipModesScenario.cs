#if SELFTEST
using Microsoft.UI.Xaml.Automation;
using TimeTug.App.Chip;
using TimeTug.Platform.Chip;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>chip-modes</c>: the real countdown chip (view model, host and window) over a scripted day on the standing clock (Friday 16 October 2026,
/// 10:20 UTC; every title and time is invented). Off shows nothing; Before meetings shows it from the chosen minutes before the meeting, with the
/// text, the screen reader's sentence and the place the placement rule gives; Always shows a meeting far away and one in progress; the window is a
/// tool window that refuses activation, follows the Light and Dark choice, is solid under reduced transparency, and uses the system colours under a
/// contrast theme. Screenshots come from the scripted data only.
/// </summary>
internal static class ChipModesScenario
{
    public const string Name = "chip-modes";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = ChipRig.Create(options, cancellation);
        var run = rig.Run;
        var primary = rig.Flyout.Monitors.Primary;

        // ---- Off is the default ----------------------------------------------------------------------------------------------
        rig.Tug.Engine.Push(rig.StateWith(70, join: "https://meet.example.test/selftest"));
        rig.Host.Start();
        await rig.Tug.Settle(500);
        run.Check(rig.Store.Load().ChipMode == ChipMode.Off, "a fresh store has the chip off");
        run.Check(!rig.Host.HasWindow && rig.Windows.Last is null, "Off shows no chip and makes no window");

        // ---- Before meetings: nothing at 70 minutes, the chip at 15 --------------------------------------------------------------
        rig.Use(ChipMode.BeforeMeetings, 15);
        await rig.Tug.Settle(300);
        run.Check(!rig.Host.HasWindow, "Before meetings, 70 minutes ahead: no chip");
        rig.Advance(TimeSpan.FromMinutes(54));
        run.Check(!rig.Host.HasWindow, "16 minutes ahead is still outside the 15 minute window");
        rig.Advance(TimeSpan.FromMinutes(1));
        var window = await rig.WaitForWindowAsync();
        run.Check(window is not null, "15 minutes ahead the chip appears");
        if (window is null)
        {
            return rig.Tug.Finish();
        }

        run.Check(window.Plan.Text == "Planning review · in 15 min", "the chip says what is coming: " + window.Plan.Text);
        run.Check(AutomationProperties.GetName(window.RootElement) == "Planning review, in 15 minutes", "one element is named with the whole sentence: " + AutomationProperties.GetName(window.RootElement));
        run.Check(ChipWindowChrome.HasChipStyles(window.Handle), "the window is a tool window that refuses activation (read back from Windows)");
        var shown = ChipWindowChrome.OuterBounds(window.Handle);
        var expected = ChipPlacement.Compute(rig.Store.Load().ChipPositions, rig.Flyout.Monitors.Monitors, rig.Flyout.Anchor.Rect, window.MeasureContent());
        run.Check(shown is { } s && Math.Abs(s.X - expected.X) <= 2 && Math.Abs(s.Y - expected.Y) <= 2 && Math.Abs(s.Width - expected.Width) <= 2, $"the chip sits where the placement rule says: shown {shown}, expected {expected}");
        run.Check(shown is { } inside && primary.Work.Contains(inside.CenterX, inside.CenterY), "the chip is on the work area of the monitor");
        run.Check(window.Activations == 0, "the window was never activated");

        // ---- the look: Light, Dark, solid, contrast ------------------------------------------------------------------------------
        rig.Store.Save(rig.Store.Load() with { Theme = AppTheme.Light });
        await rig.Tug.WaitFor(() => !window.IsDark);
        run.Check(!window.IsDark, "the Light choice gives the light chip");
        run.Check(window.Backdrop == (ChipBackdrop.IsSupported ? FlyoutBackdrop.Material : FlyoutBackdrop.Solid), "the chip is acrylic where Windows has it: " + window.Backdrop);
        await rig.Tug.Settle(600);
        await run.ShotAsync(window, "light");

        rig.Store.Save(rig.Store.Load() with { Theme = AppTheme.Dark });
        await rig.Tug.WaitFor(() => window.IsDark);
        run.Check(window.IsDark, "the Dark choice gives the dark chip");
        await rig.Tug.Settle(600);
        await run.ShotAsync(window, "dark");

        rig.Tug.Settings.TransparencyEffectsEnabled = false;
        rig.Tug.Settings.Raise();
        await rig.Tug.WaitFor(() => window.Backdrop == FlyoutBackdrop.Solid);
        run.Check(window.Backdrop == FlyoutBackdrop.Solid, "reduced transparency makes the chip solid");
        await rig.Tug.Settle(400);
        await run.ShotAsync(window, "solid");
        rig.Tug.Settings.TransparencyEffectsEnabled = true;

        rig.Tug.Settings.HighContrast = true;
        rig.Tug.Settings.Raise();
        await rig.Tug.WaitFor(() => window.IsContrast);
        run.Check(window.IsContrast && window.Backdrop == FlyoutBackdrop.Solid, "a contrast theme makes the chip solid in the system colours");
        await rig.Tug.Settle(400);
        await run.ShotAsync(window, "contrast");
        rig.Tug.Settings.HighContrast = false;
        rig.Tug.Settings.Raise();
        await rig.Tug.WaitFor(() => !window.IsContrast);
        rig.Store.Save(rig.Store.Load() with { Theme = AppTheme.UseSystem });

        // ---- the window of Before meetings is closed again by a shorter window and by the end of the meeting ------------------------------
        rig.Use(ChipMode.BeforeMeetings, 5);
        run.Check(await rig.Tug.WaitFor(() => !rig.Host.HasWindow), "choosing 5 minutes ahead closes a chip that was showing at 15");
        rig.Use(ChipMode.BeforeMeetings, 15);
        window = await rig.WaitForWindowAsync();
        run.Check(window is not null, "choosing 15 minutes brings it back");
        rig.Advance(TimeSpan.FromMinutes(15));
        run.Check(window is not null && await rig.Tug.WaitFor(() => window.Plan.Countdown.StartsWith("now", StringComparison.Ordinal)), "when the meeting starts the chip says now: " + window?.Plan.Countdown);

        // ---- Always: a meeting in progress, one far away, then Off ----------------------------------------------------------------------
        rig.Use(ChipMode.Always);
        rig.Tug.Engine.Push(rig.StateWith(-20, 50, "Design review", color: "#E8710A"));
        window = await rig.WaitForWindowAsync();
        run.Check(window is not null, "Always shows a meeting in progress");
        run.Check(window?.Plan.Countdown.StartsWith("now · ends ", StringComparison.Ordinal) == true, "it says now and when it ends: " + window?.Plan.Text);
        if (window is not null)
        {
            await run.ShotAsync(window, "running");
        }

        rig.Tug.Engine.Push(rig.StateWith(5 * 60, 30, "Offsite planning session with the platform and growth teams"));
        await rig.Tug.WaitFor(() => rig.Windows.Last?.Plan.Title.StartsWith("Offsite", StringComparison.Ordinal) == true);
        window = rig.Window;
        run.Check(window is not null && window.Plan.Countdown == "in 5 h", "Always shows a meeting five hours away: " + window?.Plan.Countdown);
        var wide = window is null ? null : ChipWindowChrome.OuterBounds(window.Handle);
        run.Check(wide is { } w && w.Width <= Math.Round(ChipPlacement.MaxWidthDips * primary.Scale) + 2, "a long title does not make the chip wider than 320 DIP: " + wide);
        if (window is not null)
        {
            await run.ShotAsync(window, "long-title");
        }

        rig.Tug.Engine.Push(rig.Flyout.State([]));
        run.Check(await rig.Tug.WaitFor(() => !rig.Host.HasWindow), "a day with nothing left hides the chip (never empty)");
        rig.Tug.Engine.Push(rig.StateWith(40));
        run.Check(await rig.WaitForWindowAsync() is not null, "a meeting comes back");
        rig.Use(ChipMode.Off);
        run.Check(await rig.Tug.WaitFor(() => !rig.Host.HasWindow), "turning the chip off closes its window");
        return rig.Tug.Finish();
    }
}
#endif
