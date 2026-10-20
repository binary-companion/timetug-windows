#if SELFTEST
using System.Globalization;
using TimeTug.App.Tray;
using TimeTug.Platform.Flyout;
using TimeTug.Platform.OsState;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Tray;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>flyout-anchor</c>: the real tray icon and the real taskbar. The icon is created, the anchor asks Windows where it is
/// (its own rectangle, the overflow button when the icon is in the overflow flyout, or the notification area), and the flyout opens
/// next to the answer without covering it and without leaving the monitor. The answer and a dump of the taskbar's UI Automation
/// elements are written to the output folder (<c>anchor.txt</c>, <c>taskbar-uia.txt</c>) for <c>docs/spikes/2026-10-flyout-anchor.md</c>.
/// With <c>--hold N</c> the flyout stays open and the anchor is asked every two seconds for N seconds (<c>anchor-log.txt</c>), so the
/// icon can be moved between the overflow and the taskbar while it runs. A runner without a taskbar has no icon; the flyout then
/// opens in the corner of the screen, which is checked too.
/// </summary>
internal static class FlyoutAnchorScenario
{
    public const string Name = "flyout-anchor";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        TrayIconService? service = null;
        var probe = new WindowsTaskbarProbe();
        var diagnostics = new RecordingDiagnostics();
        var monitorList = new StaticMonitors(MonitorService.Enumerate());
        var anchor = new TrayAnchor(() => service?.IconId, probe, monitorList, diagnostics);
        using var rig = FlyoutRig.Create(options, cancellation, anchor);
        var run = rig.Run;
        rig.Tug.Engine.Push(rig.State(rig.Day()));

        using var model = new TrayViewModel(
            rig.Tug.Engine, rig.Tug.Controller, rig.Tug.Settings, rig.Tug.Text, new TrayScenario.CountingHost(), rig.Clock, new TrayScenario.ManualTicker(), CultureInfo.GetCultureInfo("en-US"));
        using var tray = new TrayIconService(model, new DispatcherQueueDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()), diagnostics);
        service = tray;
        tray.Start();
        var created = await rig.Tug.WaitFor(() => tray.IsCreated, 5000);
        run.Info($"tray icon created: {created}; id {tray.IconId}");
        run.Check(tray.IconId is not null, "the tray icon has an identifier");

        var found = anchor.TryGetAnchor();
        run.Info($"anchor: {found?.Source.ToString() ?? "none"} {found?.Rect}");
        var text = $"created={created}\nsource={found?.Source}\nrect={found?.Rect}\nicon={(tray.IconId is { } id ? probe.IconRect(id) : null)}\noverflow button={probe.OverflowButtonRect()}\nnotification area={probe.NotificationAreaRect()}\n";
        await File.WriteAllTextAsync(Path.Combine(run.Folder, "anchor.txt"), text, cancellation);
        await File.WriteAllTextAsync(Path.Combine(run.Folder, "taskbar-uia.txt"), probe.Describe(), cancellation);
        if (created)
        {
            run.Check(found is not null, "with the icon in the notification area some anchor is found");
        }

        if (found is { } shown)
        {
            run.Check(monitorList.Monitors.Any(m => m.Bounds.Contains(shown.Rect.CenterX, shown.Rect.CenterY)), "the anchor is on a monitor");
        }

        var window = await rig.OpenAsync();
        run.Check(window is not null, "the flyout opens next to the answer");
        if (window is not null)
        {
            var outer = FlyoutWindowChrome.OuterBounds(window.Handle);
            run.Info($"flyout window {outer}");
            var home = monitorList.Monitors.FirstOrDefault(m => outer is { } o && m.Bounds.Contains(o.CenterX, o.CenterY));
            run.Check(home is not null && outer is { } w && w.X >= home.Bounds.X && w.Right <= home.Bounds.Right && w.Y >= home.Bounds.Y && w.Bottom <= home.Bounds.Bottom, "the window is on one monitor, whole");
            run.Check(found is not { } f || outer is not { } v || !Overlaps(v, f.Rect), "the window does not cover the icon or its stand-in");
            await rig.Tug.Settle(600);
            await run.ShotAsync(window, "anchored");
        }

        if (options.HoldSeconds > 0)
        {
            var log = new List<string>();
            for (var second = 0; second < options.HoldSeconds; second += 2)
            {
                var now = anchor.TryGetAnchor();
                log.Add($"{second,3}s  {now?.Source.ToString() ?? "none",-16} {now?.Rect}  icon={(tray.IconId is { } icon ? probe.IconRect(icon) : null)}");
                await File.WriteAllLinesAsync(Path.Combine(run.Folder, "anchor-log.txt"), log, cancellation);
                await Task.Delay(2000, cancellation);
            }
        }

        rig.Host.Close();
        return rig.Tug.Finish();
    }

    private static bool Overlaps(PixelRect a, PixelRect b) => a.X < b.Right && b.X < a.Right && a.Y < b.Bottom && b.Y < a.Bottom;
}
#endif
