#if SELFTEST
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TimeTug.App.Tug;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Tug;

namespace TimeTug.App.SelfTest;

/// <summary>
/// Scenario <c>tug-basic</c>: a <see cref="TugController"/> over fakes (two monitors, a request with a link and attendees) draws
/// real tug windows through <see cref="TugWindowHost"/>. It checks the windows' size and position, the controls, the
/// announcement and the automation names, then answers requests the way a person would (a key, Join and Dismiss through UI
/// Automation), and covers a failed Join (link to copy), "Click anywhere to respond", a monitor unplugged and plugged back in,
/// and the contrast, transparency-off and reduced-motion looks. It saves a screenshot of each look.
/// </summary>
internal static partial class TugBasicScenario
{
    private static readonly IReadOnlyList<TimeSpan> Snoozes = [TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(600)];
    private const string Link = "https://meet.example.test/selftest-room";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        Directory.CreateDirectory(options.OutputFolder);
        var checks = new List<string>();
        var failures = new List<string>();
        var attempts = new List<SelfTestShot>();
        void Check(bool condition, string description)
        {
            checks.Add((condition ? "ok: " : "FAIL: ") + description);
            if (!condition)
            {
                failures.Add(description);
            }
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        var time = TimeProvider.System;
        var text = new ResourceLoaderLocalizer();

        // Closing the last window of a WinUI app ends it. The real app keeps its own windows alive; this stands in for them.
        var anchor = new Window();

        using var forms = new AccountFormService(queue, text);
        using var engine = new ScriptedEngineClient(forms);
        var session = new FakeSession();
        var notifications = new FakeNotifications();
        var monitors = new FakeMonitors();
        var toasts = new FakeToasts();
        var launcher = new FakeLauncher();
        var diagnostics = new RecordingDiagnostics();
        var settings = new FakeUiSettings();
        var (first, second) = FakeMonitorLayout();
        monitors.Set(first, second);

        using var controller = new TugController(engine, session, notifications, monitors, toasts, launcher, diagnostics, time);
        using var host = new TugWindowHost(controller, new DispatcherQueueDispatcher(queue), settings, text, time, new SecondTicker(time));
        host.Start();

        async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 6000)
        {
            for (var waited = 0; waited < milliseconds; waited += 100)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100, cancellation);
            }

            return condition();
        }

        async Task Settle(int milliseconds = 1200) => await Task.Delay(milliseconds, cancellation);

        void Keyboard(string where)
        {
            var window = host.Primary;
            var activation = window is null ? string.Empty : string.Join(" | ", window.ActivationLog);
            var has = window is not null && TugWindowChrome.IsForeground(WinRT.Interop.WindowNative.GetWindowHandle(window));
            checks.Add($"info: {where}: main window foreground={has}, banner={window?.ViewModel.ShowClickToRespond}, controller={controller.State}; activation: {activation}");
        }

        async Task Shot(Window window, string name)
        {
            var (best, taken) = await SelfTestRunner.CaptureAsync(window, options.OutputFolder, name);
            attempts.AddRange(taken);
            Check(best is not null, name + ".png is not blank");
        }

        TakeoverRequest Request(string id, string title, TimeSpan startsIn, string? link = Link, int others = 5, string? calendar = "Work")
        {
            var start = time.GetUtcNow() + startsIn;
            return new TakeoverRequest(id, title, start, start.AddMinutes(30), false, startsIn <= TimeSpan.Zero, link, Snoozes, calendar, others);
        }

        async Task<bool> Present(TakeoverRequest request)
        {
            await controller.OnTakeoverDueAsync(request);
            return await WaitFor(() => host.WindowCount == monitors.Monitors.Count && host.Primary is not null);
        }

        async Task<bool> WaitHidden() => await WaitFor(() => host.WindowCount == 0);

        // ---- A: the tug itself, then a snooze from the keyboard ---------------------------------------------------
        var a = Request("e-a", "Quarterly planning review with the platform and growth teams", TimeSpan.FromSeconds(245));
        Check(await Present(a), "a due takeover opens one window per monitor");
        var primary = host.Primary!;
        var mirrors = host.Mirrors;
        var mirror = mirrors.Count > 0 ? mirrors[0] : null;
        Check(mirror is not null && host.WindowCount == 2, "there is one primary window and one mirror");
        Check(controller.State == TugState.Shown || controller.State == TugState.ShownWithoutFocus, "the controller is showing the tug");
        Check(engine.Sent.Contains("presented:e-a"), "takeover.presented was reported");
        await Settle();

        if (mirror is not null)
        {
            Check(Covers(primary.AppWindow, first), $"the primary window covers its whole monitor ({Describe(primary.AppWindow)} for {Describe(first)})");
            Check(Covers(mirror.AppWindow, second), $"the mirror covers its whole monitor ({Describe(mirror.AppWindow)} for {Describe(second)})");
            Check(mirror.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true, HasBorder: false, HasTitleBar: false }, "the mirror is borderless and topmost");
        }

        Check(primary.AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true, HasBorder: false, HasTitleBar: false }, "the primary is borderless and topmost");
        var client = TugWindowChrome.ClientSize(WinRT.Interop.WindowNative.GetWindowHandle(primary));
        Check(client == (first.Width, first.Height), $"the window has no frame: its drawable area is {client.Width}x{client.Height} for a {first.Width}x{first.Height} monitor");
        checks.Add("info: primary " + TugWindowChrome.Describe(WinRT.Interop.WindowNative.GetWindowHandle(primary)));
        Check(!primary.AppWindow.IsShownInSwitchers, "the tug has no taskbar button");
        Check(primary.Announcements.SequenceEqual([primary.ViewModel.Announcement]) && primary.ViewModel.Announcement.Contains("starts in", StringComparison.Ordinal), "Narrator was told about the tug through a UI Automation notification: " + primary.ViewModel.Announcement);
        var countdown = ScenarioUi.FindNamed<TextBlock>(primary, "CountdownValue");
        Check(countdown is not null && CountdownPattern().IsMatch(countdown.Text), "the live countdown reads like 4:03: " + countdown?.Text);
        var firstReading = countdown?.Text;
        await Settle(2300);
        Check(countdown is not null && countdown.Text != firstReading && CountdownPattern().IsMatch(countdown.Text), $"the countdown moves with the clock ({firstReading} then {countdown?.Text})");
        Check(ScenarioUi.FindNamed<TextBlock>(primary, "TitleText") is { MaxLines: 2 }, "the title is limited to two lines");
        Check(ScenarioUi.FindNamed<TextBlock>(primary, "DetailsText") is { Text: var details } && details.Contains("· Work · 6 people", StringComparison.Ordinal), "the details line has the calendar and the people: " + ScenarioUi.FindNamed<TextBlock>(primary, "DetailsText")?.Text);
        Check(ScenarioUi.HasButton(primary, "Join meeting") && ScenarioUi.HasButton(primary, "Dismiss this reminder") && ScenarioUi.HasButton(primary, "Snooze this reminder"), "Join, Snooze and Dismiss are there");
        Check(primary.ViewModel.SnoozeChoices.Select(c => c.Title).SequenceEqual(["1 minute", "5 minutes", "10 minutes"]), "the Snooze menu offers the request's own options");
        Check(ScenarioUi.Unnamed(primary).Count == 0, "every control on the primary has an automation name: " + string.Join(", ", ScenarioUi.Unnamed(primary)));
        if (mirror is not null)
        {
            Check(ScenarioUi.Unnamed(mirror).Count == 0, "every control on the mirror has an automation name");
            Check(!ScenarioUi.HasButton(mirror, "Join meeting"), "the mirror has no controls");
        }

        await Shot(primary, "tug-basic-primary");
        if (mirror is not null)
        {
            await Shot(mirror, "tug-basic-mirror");
        }

        if (options.HoldSeconds > 0)
        {
            // For a real screen capture: the windows are up, above everything, until the hold ends.
            await File.WriteAllTextAsync(Path.Combine(options.OutputFolder, "tug-basic-holding.txt"), "holding", cancellation);
            await Task.Delay(TimeSpan.FromSeconds(options.HoldSeconds), cancellation);
        }

        Check(await primary.ViewModel.HandleKeyAsync(TugKey.Digit5), "key 5 is routed to Snooze");
        Check(await WaitHidden(), "the windows close after the snooze");
        Check(engine.Sent.Contains("snooze:e-a:300"), "the engine received snooze for 5 minutes");

        // ---- B: Join by clicking, with nothing focused --------------------------------------------------------------
        Check(await Present(Request("e-b", "Design review", TimeSpan.FromSeconds(50))), "a second takeover opens");
        Check(ScenarioUi.Invoke(host.Primary!, "Join meeting"), "the Join button can be invoked");
        Check(await WaitHidden(), "the windows close after Join");
        Check(launcher.Opened.SequenceEqual([Link]) && engine.Sent.Contains("join:e-b"), "Join opened the link and told the engine");

        // ---- C: Dismiss ----------------------------------------------------------------------------------------------
        Check(await Present(Request("e-c", "Standup", TimeSpan.FromSeconds(30))), "a third takeover opens");
        Check(ScenarioUi.Invoke(host.Primary!, "Dismiss this reminder"), "the Dismiss button can be invoked");
        Check(await WaitHidden() && engine.Sent.Contains("dismiss:e-c"), "Dismiss closes the windows and tells the engine");

        // ---- D: no link: no Join, Return dismisses ------------------------------------------------------------------
        Check(await Present(Request("e-d", "Focus time", TimeSpan.FromSeconds(120), link: null, others: 0, calendar: null)), "a takeover without a link opens");
        Check(!ScenarioUi.HasButton(host.Primary!, "Join meeting"), "there is no Join button without a link");
        Check(host.Primary!.ViewModel.Hints == "Esc dismisses · 1, 5 or 0 snoozes", "the hints list only the keys that do something: " + host.Primary.ViewModel.Hints);
        Check(await host.Primary.ViewModel.HandleKeyAsync(TugKey.Return) && await WaitHidden() && engine.Sent.Contains("dismiss:e-d"), "Return dismisses when there is no link");

        // ---- E: Join fails: the link is shown to copy --------------------------------------------------------------
        launcher.Succeeds = false;
        Check(await Present(Request("e-e", "Customer call", TimeSpan.FromSeconds(-190))), "a meeting that already started opens");
        Check(host.Primary!.ViewModel.CountdownValue == "Started 3 min ago", "a late tug says how late it is: " + host.Primary.ViewModel.CountdownValue);
        Check(ScenarioUi.Invoke(host.Primary, "Join meeting"), "Join is clicked");
        Check(await WaitFor(() => host.Primary?.ViewModel.HasJoinFailed == true), "a failed Join shows the link");
        await Settle();
        Check(host.WindowCount == 2 && !engine.Sent.Any(s => s.StartsWith("join:e-e", StringComparison.Ordinal)), "the request stays up and the engine was not told to join");
        Check(ScenarioUi.FindNamed<TextBox>(host.Primary!, "JoinLinkBox") is { Text: Link }, "the link is in a box to copy");
        Check(ScenarioUi.Invoke(host.Primary!, "Copy the meeting link"), "the Copy button can be invoked");
        await Settle(600);
        await Shot(host.Primary!, "tug-basic-joinfailed");
        Check(ScenarioUi.Invoke(host.Primary!, "Dismiss this reminder") && await WaitHidden(), "the failed-join tug is dismissed");
        launcher.Succeeds = true;

        // ---- F: Windows did not hand over the keyboard ---------------------------------------------------------------
        Check(await Present(Request("e-f", "Weekly sync", TimeSpan.FromSeconds(180))), "a takeover opens for the no-focus case");
        await controller.ReportFocusAsync(false);
        Check(await WaitFor(() => host.Primary?.ViewModel.ShowClickToRespond == true), "no keyboard shows Click anywhere to respond");
        await Settle(600);
        Check(ScenarioUi.FindNamed<Border>(host.Primary!, "ClickBanner") is { Visibility: Visibility.Visible }, "the banner is visible");
        Check(controller.State == TugState.ShownWithoutFocus, "the controller records the missing keyboard");
        await Shot(host.Primary!, "tug-basic-nofocus");
        host.Primary!.TakeFocus();
        await Settle(800);
        var foreground = TugWindowChrome.IsForeground(WinRT.Interop.WindowNative.GetWindowHandle(host.Primary));
        checks.Add($"info: the main window is {(foreground ? "" : "not ")}the foreground window after asking for focus");
        if (foreground)
        {
            Check(!host.Primary.ViewModel.ShowClickToRespond && controller.State == TugState.Shown, "once the window has the keyboard the banner goes away");
        }

        await controller.ReportFocusAsync(true);

        // ---- G: monitors unplugged and plugged back in while the tug is up -------------------------------------------
        var primaryBefore = host.Primary;
        monitors.Set(first);
        await controller.OnMonitorsChangedAsync();
        Keyboard("before unplug");
        Check(await WaitFor(() => host.WindowCount == 1), "unplugging the second monitor closes only its window");
        Check(ReferenceEquals(host.Primary, primaryBefore), "the main window stays");
        monitors.Set(first, second);
        await controller.OnMonitorsChangedAsync();
        Check(await WaitFor(() => host.WindowCount == 2), "plugging it back in opens a mirror again");
        var secondAsPrimary = second with { IsPrimary = true };
        monitors.Set(secondAsPrimary);
        await controller.OnMonitorsChangedAsync();
        Check(await WaitFor(() => host.WindowCount == 1 && host.Primary is { Plan.Monitor.Id: "selftest-2" }), "when the main monitor goes, the controls move to the remaining monitor");
        Check(host.Primary is not null && ScenarioUi.HasButton(host.Primary, "Dismiss this reminder") && !ReferenceEquals(host.Primary, primaryBefore), "the new primary window has the controls");
        monitors.Set(first, second);
        await controller.OnMonitorsChangedAsync();
        Check(await WaitFor(() => host.WindowCount == 2 && host.Primary is { Plan.Monitor.Id: "selftest-1" }), "restoring the layout puts the controls back on the main monitor");

        await Settle();
        Keyboard("after the layout is restored");

        // ---- H: contrast theme, transparency off, reduced motion -----------------------------------------------------
        Check(host.Primary!.ViewModel.Backdrop == TugBackdropKind.Acrylic, "the backdrop is acrylic by default");
        settings.TransparencyEffectsEnabled = false;
        settings.Raise();
        await Settle();
        Keyboard("after the restyle");
        Check(await WaitFor(() => host.Primary?.ViewModel.Backdrop == TugBackdropKind.SolidNavy && host.WindowCount == 2), "transparency off gives solid navy");
        await Settle();
        await Shot(host.Primary!, "tug-basic-solid");
        settings.HighContrast = true;
        settings.Raise();
        Check(await WaitFor(() => host.Primary?.ViewModel.Palette == TugPalette.SystemContrast && host.WindowCount == 2), "a contrast theme switches to the system colours");
        await Settle();
        Check(host.Primary!.Announcements.Count == 0, "restyling does not announce the tug a second time");
        await Shot(host.Primary!, "tug-basic-contrast");
        settings.HighContrast = false;
        settings.TransparencyEffectsEnabled = true;
        settings.AnimationsEnabled = false;
        settings.Raise();
        Check(await WaitFor(() => host.Primary?.ViewModel.Palette == TugPalette.Brand && host.WindowCount == 2), "the brand palette returns");
        Check(host.Primary!.ViewModel.ReducedMotion && !host.Primary.ViewModel.PulseEnabled, "reduced motion turns the pulse off");
        Check(ScenarioUi.Invoke(host.Primary, "Dismiss this reminder") && await WaitHidden(), "the last tug is dismissed");

        // One terminal command per request, in the order they were answered.
        var terminal = engine.Sent.Where(s => !s.StartsWith("presented:", StringComparison.Ordinal)).ToList();
        Check(terminal.SequenceEqual(["snooze:e-a:300", "join:e-b", "dismiss:e-c", "dismiss:e-d", "dismiss:e-e", "dismiss:e-f"]), "each request received exactly one terminal command: " + string.Join(", ", terminal));
        Check(diagnostics.Entries.All(e => e.Level != "error"), "nothing was recorded as an error: " + string.Join("; ", diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Message)));

        GC.KeepAlive(anchor);
        var firstShot = File.Exists(Path.Combine(options.OutputFolder, "tug-basic-primary.png")) ? "tug-basic-primary.png" : null;
        return SelfTestRunner.Result(options, firstShot, failures.Count == 0 ? null : string.Join("; ", failures), attempts, checks);
    }

    /// <summary>Two monitors for the plan: the real ones when the machine has two, otherwise the real main display and a smaller invented one beside it.</summary>
    private static (MonitorInfo Main, MonitorInfo Second) FakeMonitorLayout()
    {
        DisplayArea? main = null;
        DisplayArea? other = null;
        var areas = DisplayArea.FindAll();
        for (var i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            if (area.IsPrimary)
            {
                main ??= area;
            }
            else
            {
                other ??= area;
            }
        }

        var mainBounds = (main ?? DisplayArea.Primary).OuterBounds;
        var first = new MonitorInfo("selftest-1", mainBounds.X, mainBounds.Y, mainBounds.Width, mainBounds.Height, true);
        if (other is not null)
        {
            var b = other.OuterBounds;
            return (first, new MonitorInfo("selftest-2", b.X, b.Y, b.Width, b.Height, false));
        }

        // One display only (a hosted runner): the second "monitor" is a 4:3 rectangle inside it. It overlaps the first window,
        // but each window is still captured on its own.
        var width = mainBounds.Width * 3 / 4;
        var height = mainBounds.Height * 3 / 4;
        return (first, new MonitorInfo("selftest-2", mainBounds.X + (mainBounds.Width / 8), mainBounds.Y + (mainBounds.Height / 8), width, height, false));
    }

    private static bool Covers(AppWindow window, MonitorInfo monitor) =>
        window.Position.X == monitor.X && window.Position.Y == monitor.Y && window.Size.Width == monitor.Width && window.Size.Height == monitor.Height;

    private static string Describe(AppWindow window) => $"{window.Size.Width}x{window.Size.Height} at {window.Position.X},{window.Position.Y}";

    private static string Describe(MonitorInfo monitor) => $"{monitor.Width}x{monitor.Height} at {monitor.X},{monitor.Y}";

    [GeneratedRegex(@"^\d+:\d\d$")]
    private static partial Regex CountdownPattern();
}
#endif
