#if SELFTEST
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Tray;
using TimeTug.Platform.Capture;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Theming;
using TimeTug.Presentation.Tray;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>menu-theme</c>: the right-click menus follow the app's own theme choice, not the colour Windows gives a process that never asked.
/// The real tray icon is right-clicked with real input and the native menu Windows draws for it is read off the screen: dark under Dark (whatever the
/// PC's own theme), light under Light, the apps theme under "Use system", and again after a change of the choice with the icon still there. A contrast
/// theme is left to Windows. The menus of the app's own windows are measured too (the theme of the popup, and its text colour).
/// </summary>
internal static class MenuThemeScenario
{
    public const string Name = "menu-theme";

    private const double DarkBelow = 90;
    private const double LightAbove = 110;
    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        var queue = DispatcherQueue.GetForCurrentThread();
        var anchor = new Window();
        await TrayMenusAsync(run, queue, cancellation);
        await XamlMenusAsync(run, cancellation);
        anchor.Close();
        return run.Finish();
    }

    // ---- the tray icon's menu (native) ------------------------------------------------------------------------------------

    private static async Task TrayMenusAsync(ScenarioRun run, DispatcherQueue queue, CancellationToken cancellation)
    {
        var text = new ResourceLoaderLocalizer();
        using var forms = new AccountFormService(queue, text);
        using var engine = new ScriptedEngineClient(forms);
        var store = new MemoryPresentationSettingsStore();
        var ui = new FakeUiSettings { AppsUseDarkTheme = true };
        var diagnostics = new RecordingDiagnostics();
        using var model = new TrayViewModel(
            engine, new TrayScenario.FakeAttention(), ui, text, new TrayScenario.CountingHost(), new TrayScenario.FixedTime(Noon), new TrayScenario.ManualTicker(),
            CultureInfo.GetCultureInfo("en-US"), store);
        engine.Push(Scenes.Connected(Noon));
        using var tray = new TrayIconService(model, new DispatcherQueueDispatcher(queue), diagnostics);
        tray.Start();
        var created = await WaitFor(() => tray.IsCreated, 5000);
        run.Info($"tray icon created: {created}, identifier {tray.IconId}");
        var probe = new WindowsTaskbarProbe();
        if (!created || tray.IconId is not { } id)
        {
            run.Info("SKIPPED the native menu steps: this machine has no notification area to put the icon in");
            return;
        }

        async Task<RegionCapture?> OpenAsync(string label)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var rect = probe.IconRect(id);
                if (rect is null && probe.OverflowButtonRect() is { } chevron)
                {
                    MouseInput.ClickAt((int)chevron.CenterX, (int)chevron.CenterY);
                    await Task.Delay(900, cancellation);
                    rect = probe.IconRect(id);
                }

                if (rect is not { } at)
                {
                    run.Info($"{label}: attempt {attempt + 1}: the icon is nowhere to be found");
                    await Task.Delay(500, cancellation);
                    continue;
                }

                MouseInput.RightClickAt((int)at.CenterX, (int)at.CenterY);
                if (await WaitFor(() => ScreenRegion.OpenNativeMenu() is { Height: > 120 }, 3000))
                {
                    // The menu opens with a short animation; read it when it has stopped moving.
                    await Task.Delay(900, cancellation);
                    if (ScreenRegion.OpenNativeMenu() is { } menu)
                    {
                        run.Info($"{label}: menu at {menu}, icon at {at}");
                        var shot = await run.RegionShotAsync(menu, label);
                        await CloseMenusAsync(cancellation);
                        return shot;
                    }
                }

                run.Info($"{label}: attempt {attempt + 1}: no menu after a right click at {at}; windows of the menu class: {ScreenRegion.DescribeMenus()}");
                await run.RegionShotAsync(new PixelRect(Math.Max(0, at.X - 900), Math.Max(0, at.Y - 700), 1000, 800), label + "-missing", required: false);
                await CloseMenusAsync(cancellation);
            }

            return null;
        }

        // What the person saw before this was fixed: a process that never asked for dark menus gets Windows' plain light one, on a dark PC too.
        var before = await OpenAsync("tray-menu-before-the-follower");
        run.Info($"the menu before the follower exists (the PC's apps theme is {(IsSystemDark() ? "dark" : "light")}): mean brightness {before?.MeanLuminance:0}");

        using var follower = new NativeMenuThemeFollower(store, ui, new UxThemeNativeMenuTheme(), diagnostics);
        run.Check(follower.Mode == NativeMenuMode.Dark, "the follower set Dark at the start (the apps theme is dark): " + follower.Mode);
        var any = await OpenAsync("tray-menu-dark");
        if (any is null)
        {
            run.Info("SKIPPED the native menu steps: the icon could not be right-clicked on this machine (it is not on the taskbar or in its overflow)");
            return;
        }

        run.Info($"the tray menu under Dark, the apps theme dark: mean brightness {any.MeanLuminance:0}");
        run.Check(any.MeanLuminance < DarkBelow, $"Dark: the menu Windows draws for the icon is dark (mean {any.MeanLuminance:0})");

        store.Save(store.Load() with { Theme = AppTheme.Light });
        var light = await OpenAsync("tray-menu-light");
        run.Check(light is { MeanLuminance: > LightAbove }, $"Light chosen, the apps theme still dark: the menu is light, live, without a restart (mean {light?.MeanLuminance:0})");

        store.Save(store.Load() with { Theme = AppTheme.Dark });
        ui.AppsUseDarkTheme = false;
        ui.Raise();
        var dark = await OpenAsync("tray-menu-dark-on-light-system");
        run.Check(dark is { MeanLuminance: < DarkBelow }, $"Dark chosen, the apps theme light: the menu is dark (mean {dark?.MeanLuminance:0})");

        store.Save(store.Load() with { Theme = AppTheme.UseSystem });
        var system = await OpenAsync("tray-menu-system-light");
        run.Check(system is { MeanLuminance: > LightAbove }, $"Use system, the apps theme light: the menu is light (mean {system?.MeanLuminance:0})");
        ui.AppsUseDarkTheme = true;
        ui.Raise();
        system = await OpenAsync("tray-menu-system-dark");
        run.Check(system is { MeanLuminance: < DarkBelow }, $"Use system, the apps theme changes to dark while the icon is up: the menu follows (mean {system?.MeanLuminance:0})");

        ui.HighContrast = true;
        ui.Raise();
        run.Check(follower.Mode == NativeMenuMode.Windows, "a contrast theme leaves the menu to Windows: " + follower.Mode);
        ui.HighContrast = false;
        ui.Raise();
        run.Check(follower.Mode == NativeMenuMode.Dark, "and back: " + follower.Mode);
    }

    private static bool IsSystemDark() =>
        Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;

    private static async Task CloseMenusAsync(CancellationToken cancellation)
    {
        for (var i = 0; i < 3 && (ScreenRegion.OpenNativeMenu() is not null || i == 0); i++)
        {
            KeyboardInput.Chord([], 0x1B);
            await Task.Delay(400, cancellation);
        }

        await Task.Delay(600, cancellation);
    }

    // ---- the menus of the app's own windows (XAML) -----------------------------------------------------------------------------

    private static async Task XamlMenusAsync(ScenarioRun run, CancellationToken cancellation)
    {
        // A window forced to each theme, whatever the application started in: the menus of both kinds follow the window, not the application.
        foreach (var theme in new[] { ElementTheme.Light, ElementTheme.Dark })
        {
            var button = new Button { Content = "Open", Margin = new Thickness(20) };
            var combo = new ComboBox { Margin = new Thickness(20), Width = 160, ItemsSource = new[] { "One", "Two" } };
            var root = new Grid { Children = { new StackPanel { Children = { button, combo } } }, RequestedTheme = theme, Background = new SolidColorBrush(theme == ElementTheme.Dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White) };
            var window = new Window { Content = root, Title = "menu-theme " + theme };
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 500));
            window.Activate();
            await SelfTestRunner.WaitForFrameAsync(window, cancellation, 900);
            run.Info($"{theme} window: the application is {Application.Current.RequestedTheme}");

            foreach (var constrained in new[] { true, false })
            {
                var name = $"{theme} window, menu{(constrained ? string.Empty : " in a window of its own")}";
                var sub = new MenuFlyoutSubItem { Text = "More" };
                sub.Items.Add(new MenuFlyoutItem { Text = "Inner" });
                var menu = new MenuFlyout { ShouldConstrainToRootBounds = constrained };
                menu.Items.Add(new MenuFlyoutItem { Text = "Alpha" });
                menu.Items.Add(sub);
                menu.ShowAt(button);
                await WaitFor(() => MenuThemeCheck.OpenMenus(root).Count > 0, 4000);
                await Task.Delay(500, cancellation);
                MenuThemeCheck.Follows(run, root, name);
                await Shot(run, window, $"xaml-{theme}-menu{(constrained ? string.Empty : "-own-window")}");
                menu.Hide();
                await Task.Delay(500, cancellation);
            }

            combo.IsDropDownOpen = true;
            await WaitFor(() => MenuThemeCheck.OpenMenus(root).Count > 0, 4000);
            await Task.Delay(500, cancellation);
            MenuThemeCheck.Follows(run, root, $"{theme} window, combo box");
            await Shot(run, window, $"xaml-{theme}-combo");
            combo.IsDropDownOpen = false;
            await Task.Delay(300, cancellation);
            window.Close();
        }
    }

    private static async Task Shot(ScenarioRun run, Window window, string label)
    {
        var p = window.AppWindow.Position;
        var s = window.AppWindow.Size;
        await run.RegionShotAsync(new PixelRect(p.X, p.Y, s.Width, s.Height), label, required: false);
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int milliseconds)
    {
        var until = Environment.TickCount64 + milliseconds;
        while (Environment.TickCount64 < until)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return condition();
    }
}
#endif
