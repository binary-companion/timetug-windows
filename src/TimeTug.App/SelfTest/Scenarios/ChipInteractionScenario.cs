#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Platform.Capture;
using TimeTug.Platform.Chip;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Flyout;
using WinRT.Interop;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>chip-interaction</c>: real mouse input on the real chip window. A left click opens the flyout; a right click opens the menu, whose items
/// join the meeting, open the Appearance settings and hide the chip until the next meeting; a drag moves the chip and the place is remembered for its
/// monitor; a double click puts it back. Throughout, the chip never becomes the foreground window (the foreground is sampled every few
/// milliseconds), never raises an activation, and the activation guard shows it refused requests (so the check cannot pass by accident). The tug
/// hides the chip and its dismissal brings it back; a full-screen app hides it within one poll; the screen reader is told 30 minutes before, once.
/// </summary>
internal static class ChipInteractionScenario
{
    public const string Name = "chip-interaction";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = ChipRig.Create(options, cancellation);
        var run = rig.Run;
        var main = rig.Flyout.Monitors.Primary;

        // Another window of this app has the keyboard, so there is a foreground window the chip must not take.
        var blocker = new Window { Title = "Chip scenario window" };
        blocker.Activate();
        await rig.Tug.Settle(900);
        var blockerHandle = WindowNative.GetWindowHandle(blocker);
        var foregroundWorks = ChipWindowChrome.ForegroundWindow() == blockerHandle;
        run.Info("another window of the app is the foreground window: " + foregroundWorks);

        rig.Use(ChipMode.Always);
        rig.Tug.Engine.Push(rig.StateWith(40, join: "https://meet.example.test/selftest-chip"));
        rig.Host.Start();
        var window = await rig.WaitForWindowAsync();
        run.Check(window is not null, "the chip opens");
        if (window is null)
        {
            blocker.Close();
            return rig.Tug.Finish();
        }

        var handle = window.Handle;
        run.Check(ChipWindowChrome.HasChipStyles(handle), "tool window, no app window, no activation: styles read back from Windows");
        var foreground = new List<nint>();
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(
            () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    lock (foreground)
                    {
                        foreground.Add(ChipWindowChrome.ForegroundWindow());
                    }

                    Thread.Sleep(4);
                }
            },
            CancellationToken.None);
        var vetoesBefore = NoActivateGuard.Vetoes;

        (int X, int Y) Centre()
        {
            var b = ChipWindowChrome.OuterBounds(handle)!.Value;
            return ((int)b.CenterX, (int)b.CenterY);
        }

        // ---- left click: the flyout opens ----------------------------------------------------------------------------------------
        var (cx, cy) = Centre();
        run.Check(MouseInput.ClickAt(cx, cy), "a real left click is delivered on the chip");
        run.Check(await rig.Tug.WaitFor(() => rig.Flyout.Host.IsOpen), "the click opened the flyout");
        rig.Flyout.Host.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);

        // ---- right click: the menu ----------------------------------------------------------------------------------------------------
        blocker.Activate();
        await rig.Tug.Settle(700);
        (cx, cy) = Centre();
        var menus = window.MenusOpened;
        run.Check(MouseInput.RightClickAt(cx, cy), "a real right click is delivered");
        run.Check(await rig.Tug.WaitFor(() => window.MenusOpened > menus), "the menu opens");
        if (foregroundWorks)
        {
            run.Check(ChipWindowChrome.ForegroundWindow() == blockerHandle, "the open menu left the keyboard with the other window");
        }

        run.Check(window.LastMenuItems.Select(i => i.Text).SequenceEqual(["Join", "Hide until the next meeting", "Open Settings"]), "the menu has Join, Hide until the next meeting and Open Settings: " + string.Join(", ", window.LastMenuItems.Select(i => i.Text)));
        run.Check(Invoke(window.LastMenuItems[0]) && await rig.Tug.WaitFor(() => rig.Joined.Count == 1), "Join opens the meeting through the flyout's path");
        run.Check(rig.Joined.SequenceEqual(["chip-1"]), "it joined the chip's meeting: " + string.Join(", ", rig.Joined));
        await rig.Tug.Settle(300);

        (cx, cy) = Centre();
        menus = window.MenusOpened;
        MouseInput.RightClickAt(cx, cy);
        await rig.Tug.WaitFor(() => window.MenusOpened > menus);
        run.Check(Invoke(window.LastMenuItems[^1]) && await rig.Tug.WaitFor(() => rig.SettingsOpened == 1), "Open Settings asks for the Appearance pane");
        await rig.Tug.Settle(300);

        // ---- drag: the place is remembered for the monitor ----------------------------------------------------------------------------
        (cx, cy) = Centre();
        var before = ChipWindowChrome.OuterBounds(handle)!.Value;
        var (toX, toY) = (main.Work.X + 300, main.Work.Y + 260);
        // The drag runs off the UI thread: the window must be free to see each move while the button is held.
        run.Check(await Task.Run(() => MouseInput.DragFromTo(cx, cy, toX, toY), CancellationToken.None), "a real drag is delivered");
        run.Check(await rig.Tug.WaitFor(() => rig.Store.Load().ChipPositions.Count == 1), "the drop is remembered");
        var after = ChipWindowChrome.OuterBounds(handle)!.Value;
        run.Check(Math.Abs((after.X - before.X) - (toX - cx)) <= 2 && Math.Abs((after.Y - before.Y) - (toY - cy)) <= 2, $"the chip followed the pointer ({before} to {after})");
        run.Check(rig.Store.Load().ChipPositions is [{ } place] && place.MonitorId == main.Id, "the place belongs to the monitor it was dropped on");
        run.Check(!rig.Flyout.Host.IsOpen, "a drag is not a click");
        await rig.Tug.Settle(600);
        await run.ShotAsync(window, "dragged");

        // ---- double click: back to the default place ----------------------------------------------------------------------------------
        (cx, cy) = Centre();
        run.Check(MouseInput.DoubleClickAt(cx, cy), "a real double click is delivered");
        run.Check(await rig.Tug.WaitFor(() => rig.Store.Load().ChipPositions.Count == 0), "a double click forgets the place");
        var home = ChipPlacement.Compute([], rig.Flyout.Monitors.Monitors, rig.Flyout.Anchor.Rect, window.MeasureContent());
        run.Check(await rig.Tug.WaitFor(() => ChipWindowChrome.OuterBounds(handle) is { } b && Math.Abs(b.X - home.X) <= 2 && Math.Abs(b.Y - home.Y) <= 2), "the chip is back in its default place");
        rig.Flyout.Host.Close();
        await Task.Delay(FlyoutHost.ClickGrace + TimeSpan.FromMilliseconds(100), cancellation);

        // ---- the tug hides the chip and its dismissal brings it back ----------------------------------------------------------------
        run.Check(await rig.Tug.PresentAsync(rig.Tug.Request("e-chip", "Weekly sync", TimeSpan.FromSeconds(180))), "a takeover opens");
        run.Check(await rig.Tug.WaitFor(() => !rig.Host.IsShown), "the chip is hidden while the tug shows");
        run.Check(!window.AppWindow.IsVisible, "its window is not visible");
        run.Check(ScenarioUi.Invoke(rig.Tug.Host.Primary!, "Dismiss this reminder"), "the tug is dismissed");
        run.Check(await rig.Tug.WaitHiddenAsync(), "the tug closes");
        run.Check(await rig.Tug.WaitFor(() => rig.Host.IsShown), "the chip comes back");

        // ---- a full-screen app hides it within one poll -----------------------------------------------------------------------------------
        rig.Tug.Notifications.CanShowFullScreen = false;
        rig.Poll.Tick();
        run.Check(await rig.Tug.WaitFor(() => !rig.Host.IsShown), "a full-screen app hides the chip at the next poll");
        rig.Tug.Notifications.CanShowFullScreen = true;
        rig.Poll.Tick();
        run.Check(await rig.Tug.WaitFor(() => rig.Host.IsShown), "and it returns when the full-screen app ends");

        // ---- the screen reader is told 30 minutes before, once ----------------------------------------------------------------------------
        rig.Advance(TimeSpan.FromMinutes(10));
        await rig.Tug.WaitFor(() => window.Announced.Count > 0);
        rig.Advance(TimeSpan.FromMinutes(1));
        await rig.Tug.Settle(300);
        run.Check(window.Announced.SequenceEqual(["Planning review starts in 30 minutes"]), "the chip announced 30 minutes before, once: " + string.Join(" | ", window.Announced));

        // ---- hide until the next meeting ----------------------------------------------------------------------------------------------------
        (cx, cy) = Centre();
        menus = window.MenusOpened;
        MouseInput.RightClickAt(cx, cy);
        await rig.Tug.WaitFor(() => window.MenusOpened > menus);
        run.Check(Invoke(window.LastMenuItems[1]) && await rig.Tug.WaitFor(() => !rig.Host.HasWindow), "Hide until the next meeting closes the chip");
        rig.Tug.Engine.Push(rig.StateWith(120, id: "chip-2", title: "Review"));
        run.Check(await rig.WaitForWindowAsync() is not null, "a different meeting brings it back");

        // ---- it never took the foreground ---------------------------------------------------------------------------------------------------------------
        await stop.CancelAsync();
        await sampler;
        int sampled;
        bool chipWasForeground;
        lock (foreground)
        {
            sampled = foreground.Count;
            chipWasForeground = foreground.Contains(handle);
        }

        run.Info($"the foreground window was sampled {sampled} times; the guard refused {NoActivateGuard.Vetoes - vetoesBefore} requests");
        run.Check(!chipWasForeground, "the chip was never the foreground window, through a click, a menu, a drag and a double click");
        run.Check(window.Activations == 0, "the chip raised no Activated event");
        if (foregroundWorks)
        {
            run.Check(NoActivateGuard.Vetoes > vetoesBefore, "the guard refused at least one activation or focus request (the check cannot pass by accident)");
        }
        else
        {
            run.Info("SKIPPED the guard-refused check: this machine never made another window the foreground window");
        }

        blocker.Close();
        return rig.Tug.Finish();
    }

    private static bool Invoke(MenuFlyoutItem item)
    {
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(item);
        if (peer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
        {
            return false;
        }

        invoke.Invoke();
        return true;
    }
}
#endif
