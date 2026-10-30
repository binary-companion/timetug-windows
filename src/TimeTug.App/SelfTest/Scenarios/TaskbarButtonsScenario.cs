#if SELFTEST
using Microsoft.UI;
using Microsoft.UI.Windowing;
using TimeTug.Platform.Capture;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Settings;
using WinRT.Interop;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>taskbar-buttons</c>: TimeTug lives in the notification area, so a taskbar button (and an Alt+Tab entry) may exist only while a
/// normal window, Settings or the welcome window, is open. The scenario runs the real app over the real engine and, in each state, lists this
/// process's top-level windows with their visibility and extended styles (<see cref="WindowAudit"/>) and asks Windows' rule which of them get a
/// taskbar presence (visible, not cloaked, and either <c>WS_EX_APPWINDOW</c> or neither owned nor <c>WS_EX_TOOLWINDOW</c>). It must be exactly the
/// window that state opened: right after launch none (the hidden anchor window and the hidden window that receives the hotkey and session
/// messages), then Settings alone, nothing again, the welcome window alone, nothing, the flyout open (none), and the tug on both monitors (none).
/// UI Automation on the taskbar (<c>Shell_TrayWnd</c>) reports the names of buttons that mention TimeTug as a second witness, when no other copy
/// of the app already has one.
/// </summary>
internal static class TaskbarButtonsScenario
{
    public const string Name = "taskbar-buttons";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        await using var rig = await EngineRig.StartAsync(options, cancellation);
        var run = rig.Run;
        if (!rig.Ready)
        {
            return rig.Finish();
        }

        var seen = await TaskbarVisibleAsync(rig);
        var before = Buttons();
        run.Info("taskbar buttons that mention TimeTug before the first window: " + Describe(before));
        var witness = seen && before is { Count: 0 };
        if (!witness)
        {
            run.Info(seen
                ? "another copy of the app is on the taskbar already, so the buttons are listed but not asserted"
                : "UI Automation sees nothing on the taskbar (hidden, or Explorer did not answer), so only the windows' own styles are asserted");
        }

        // ---- right after launch: the tray only --------------------------------------------------------------------------------------
        await rig.Settle(1000);
        await AuditAsync(rig, witness, "launched, tray only", []);
        var hidden = WindowAudit.OfThisProcess();
        run.Check(hidden.Count > 0 && hidden.All(w => !w.Visible), $"every window of the process is hidden right after launch ({hidden.Count} window(s))");
        run.Check(hidden.Any(w => w.ToolWindow && w.NoActivate), "the hidden window that receives the hotkey and session messages is a tool window that does not activate");

        // ---- Settings -----------------------------------------------------------------------------------------------------------------
        var settings = await rig.OpenSettingsAsync(SettingsPane.General);
        run.Check(settings is not null, "Settings opens");
        if (settings is null)
        {
            return rig.Finish();
        }

        await rig.Settle(1200);
        await AuditAsync(rig, witness, "Settings open", [WindowNative.GetWindowHandle(settings)]);
        settings.Close();
        run.Check(await rig.WaitFor(() => rig.Shell.Settings is { IsOpen: false }), "Settings closes");
        await rig.Settle(1000);
        await AuditAsync(rig, witness, "Settings closed", []);

        // ---- the welcome window ---------------------------------------------------------------------------------------------------------
        rig.Shell.ShowWelcome();
        var welcome = rig.Shell.Welcome;
        run.Check(welcome is not null, "the welcome window opens");
        if (welcome is null)
        {
            return rig.Finish();
        }

        await rig.Settle(1200);
        await AuditAsync(rig, witness, "welcome window open", [WindowNative.GetWindowHandle(welcome)]);
        welcome.Close();
        run.Check(await rig.WaitFor(() => rig.Shell.Welcome is null), "the welcome window closes");
        await rig.Settle(1000);
        await AuditAsync(rig, witness, "welcome window closed", []);

        // ---- the flyout ---------------------------------------------------------------------------------------------------------------------
        run.Check(rig.Shell.Flyout is { } flyout && flyout.Show(), "the flyout opens");
        await rig.Settle(1200);
        await AuditAsync(rig, witness, "flyout open", []);
        rig.Shell.Flyout?.Close();
        await rig.Settle(1000);

        // ---- the tug, on every monitor ------------------------------------------------------------------------------------------------------
        await rig.AdvanceAsync(TimeSpan.FromMinutes(16));
        if (run.Check(await rig.WaitFor(() => rig.Windows.Primary is not null && rig.Windows.WindowCount == rig.Monitors.Monitors.Count), "the tug appears on every monitor"))
        {
            await rig.Settle(1200);
            await AuditAsync(rig, witness, "tug open on every monitor", []);
            run.Check(ScenarioUi.Invoke(rig.Windows.Primary!, "Join meeting"), "Join closes the tug");
            run.Check(await rig.WaitHiddenAsync(), "the tug windows close");
            await rig.Settle(1000);
            await AuditAsync(rig, witness, "tug closed", []);
        }

        return rig.Finish();
    }

    /// <summary>Lists the process's windows in a state and checks that exactly the expected ones have a taskbar presence.</summary>
    private static async Task AuditAsync(EngineRig rig, bool witness, string state, nint[] expected)
    {
        var run = rig.Run;
        var windows = WindowAudit.OfThisProcess();
        foreach (var window in windows)
        {
            run.Info($"[{state}] {window} switchers={InSwitchers(window)}");
        }

        var present = windows.Where(InSwitchers).Select(w => w.Handle).Order().ToList();
        run.Check(present.SequenceEqual(expected.Order()), $"{state}: the windows that get a taskbar button and an Alt+Tab entry are exactly the expected ones ({present.Count} of {expected.Length})");
        // The style alone must say the same (the tug and the flyout are tool windows; AppWindow.IsShownInSwitchers is a request Explorer forgets when it restarts).
        var byStyle = windows.Where(w => w.HasTaskbarPresence).Select(w => w.Handle).Order().ToList();
        run.Check(byStyle.SequenceEqual(expected.Order()), $"{state}: and so do the windows' own styles, without AppWindow.IsShownInSwitchers ({byStyle.Count} of {expected.Length})");

        if (WindowsTaskbarProbe.ElementClassCounts() is { } classes)
        {
            run.Info($"[{state}] taskbar element classes: " + string.Join(", ", classes.OrderByDescending(c => c.Value).Select(c => $"{c.Key}={c.Value}")));
        }

        // The taskbar itself, a moment after the change (Explorer updates its buttons asynchronously).
        await rig.Settle(600);
        if (witness)
        {
            await TaskbarVisibleAsync(rig);
        }

        var buttons = Buttons();
        run.Info($"[{state}] taskbar buttons that mention TimeTug: " + Describe(buttons));
        if (witness && buttons is not null)
        {
            run.Check(buttons.Count == (expected.Length == 0 ? 0 : 1), $"{state}: the taskbar shows {(expected.Length == 0 ? "no TimeTug button" : "one TimeTug button")}: {buttons.Count}");
        }
    }

    /// <summary>
    /// True when the shell lists the window in the taskbar and Alt+Tab: the style rule says so (<see cref="TopLevelWindow.HasTaskbarPresence"/>) and,
    /// for a WinUI window, <c>AppWindow.IsShownInSwitchers</c> has not been turned off (the tug does that; Windows then keeps its button away without
    /// a tool-window style).
    /// </summary>
    private static bool InSwitchers(TopLevelWindow window)
    {
        if (!window.HasTaskbarPresence)
        {
            return false;
        }

        return AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(window.Handle))?.IsShownInSwitchers ?? true;
    }

    /// <summary>
    /// True when UI Automation can see the taskbar's elements. An auto-hidden taskbar has none until the pointer touches the bottom edge, so the
    /// pointer is put there once (it moves nothing else; the scenario's windows are not under it) and the taskbar is asked again.
    /// </summary>
    private static async Task<bool> TaskbarVisibleAsync(EngineRig rig)
    {
        if (WindowsTaskbarProbe.ElementClassCounts() is { Count: > 0 })
        {
            return true;
        }

        var screen = DisplayArea.Primary.OuterBounds;
        MouseInput.MoveTo(screen.X + (screen.Width / 2), screen.Y + screen.Height - 1);
        await rig.Settle(1500);
        return WindowsTaskbarProbe.ElementClassCounts() is { Count: > 0 };
    }

    /// <summary>The app buttons on the taskbar that mention TimeTug (not the notification-area icon); null when UI Automation did not answer.</summary>
    private static List<TaskbarMatch>? Buttons() => WindowsTaskbarProbe.ButtonNamesContaining("TimeTug")?.Where(m => !m.IsTrayIcon).ToList();

    private static string Describe(List<TaskbarMatch>? buttons) =>
        buttons is null ? "(UI Automation did not answer)" : buttons.Count == 0 ? "none" : string.Join("; ", buttons.Select(b => $"'{b.Name}' [{b.ClassName}, id={b.AutomationId}]"));
}
#endif
