#if SELFTEST
using CommunityToolkit.WinUI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Flyout;
using TimeTug.App.Settings;
using TimeTug.Platform.OsState;
using TimeTug.Platform.Capture;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tray;
using Windows.Graphics;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>settings-shell</c>: the real app shell with the engine, the OS state and the presentation store scripted, and the real Settings
/// window. It checks, in the order a person meets them: the tray's Settings row and the flyout's gear open the window once (a second request
/// focuses it); the five panes open in the Mac's order and each is named; General's hub drills into About and back; the search finds a
/// control, a keyword and a calendar (scripted names only), offers "No results", reveals the pane and tints the control for 1.5 s, fading
/// unless motion is reduced; the window comes back where it was left and an off-screen place is discarded; every control has a name; quitting
/// closes the window. It also tries the Windows Community Toolkit's settings controls (card, expander) under this Windows App SDK.
/// </summary>
internal static class SettingsShellScenario
{
    public const string Name = "settings-shell";

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        FlyoutWindow.IgnoreSystemDeactivation = true;

        // Polls until the condition holds or the time (wall clock, not a count of polls) is up: a stalled UI thread on a slow
        // runner makes the wait longer, never the check flakier.
        async Task<bool> WaitFor(Func<bool> condition, int milliseconds = 8000)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds)
            {
                if (condition())
                {
                    return true;
                }

                await Task.Delay(25, cancellation);
            }

            return condition();
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        var diagnostics = new RecordingDiagnostics();
        var text = new ResourceLoaderLocalizer();
        using var forms = new AccountFormService(queue, text);
        using var engine = new ScriptedEngineClient(forms);
        var store = new MemoryPresentationSettingsStore();
        var ui = new FakeUiSettings();
        var shell = new AppShell(queue, diagnostics, services =>
        {
            services.AddSingleton<IEngineClient>(engine);
            services.AddSingleton<ISessionState>(new FakeSession());
            services.AddSingleton<INotificationState>(new FakeNotifications());
            services.AddSingleton<IToastService>(new FakeToasts());
            services.AddSingleton<IUrlLauncher>(new FakeLauncher());
            services.AddSingleton<IHotkeyStore>(new NoHotkeyStore());
            services.AddSingleton<IPresentationSettingsStore>(store);
            services.AddSingleton<IUiSettings>(ui);
        }, withTray: true, instance: null);

        var started = shell.RunAsync();
        run.Check(await WaitFor(() => shell.TrayModel is not null && shell.Settings is not null), "the shell built the services and the tray");
        engine.Push(SettingsRig.State(DateTimeOffset.UtcNow));
        await started.WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        run.Check(!shell.Failed, "the app started" + (shell.FailureReason is null ? string.Empty : ": " + shell.FailureReason));
        if (shell.TrayModel is not { } tray || shell.Settings is not { } settings || shell.Flyout is not { } flyout)
        {
            return run.Finish("the shell did not start");
        }

        var factory = shell.Services.GetRequiredService<SettingsWindowFactory>();
        var monitors = shell.Services.GetRequiredService<IMonitorService>();
        var panes = new SettingsText(text);

        // ---- 1. the tray's Settings row opens the window, once ------------------------------------------------------------
        run.Check(tray.Current.Rows.Single(r => r.Command == TrayCommand.Settings).IsEnabled, "the tray's Settings row is enabled");
        await tray.InvokeAsync(TrayCommand.Settings);
        run.Check(await WaitFor(() => settings.IsOpen && factory.Last is not null), "the tray's Settings row opens the Settings window");
        var window = factory.Last!.Window;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation, 900);
        run.Check(window.Title == "Settings", "the window is titled Settings: " + window.Title);
        run.Check(window.Shell.SelectedPane == SettingsPane.General, "it opens on General");
        var bounds = new PixelRect(window.AppWindow.Position.X, window.AppWindow.Position.Y, window.AppWindow.Size.Width, window.AppWindow.Size.Height);
        run.Check(monitors.Monitors.Any(m => new PixelRect(bounds.X, bounds.Y, bounds.Width, 48) is var bar && Overlap(bar, m.Work) > 0), "it opens on a display: " + bounds);
        run.Check(window.AppWindow.Presenter is OverlappedPresenter { IsResizable: true, IsMaximizable: true, IsMinimizable: true }, "it is a normal window (resizable, minimizable, maximizable)");
        run.Check(window.SystemBackdrop is MicaBackdrop, "Mica is behind it");
        await tray.InvokeAsync(TrayCommand.Settings);
        await Task.Delay(400, cancellation);
        run.Check(factory.Created == 1 && settings.IsOpen, "a second Settings request focuses the open window instead of making another: " + factory.Created);
        await run.ShotAsync(window, "general");

        // ---- 2. the five panes -------------------------------------------------------------------------------------------
        var expected = new[] { "General", "Appearance", "Accounts", "Calendars", "Tug Rules" };
        var items = window.Navigation.MenuItems.OfType<NavigationViewItem>().ToList();
        run.Check(items.Select(i => i.Content as string).SequenceEqual(expected), "the navigation lists the five panes in the Mac's order: " + string.Join(", ", items.Select(i => i.Content)));
        run.Check(items.All(i => AutomationProperties.GetName(i).Length > 0 && i.Icon is not null), "every pane has a name and an icon");
        run.Check(AutomationProperties.GetName(window.SearchBox) == "Search settings", "the search box is named: " + AutomationProperties.GetName(window.SearchBox));
        foreach (var pane in Enum.GetValues<SettingsPane>())
        {
            window.Shell.SelectedPane = pane;
            await WaitFor(() => ReferenceEquals(window.Navigation.SelectedItem, items[(int)pane]) && HeadingOf(window.CurrentPage) == panes.PaneTitle(pane), 4000);
            var heading = HeadingOf(window.CurrentPage);
            run.Check(ReferenceEquals(window.Navigation.SelectedItem, items[(int)pane]) && heading == panes.PaneTitle(pane), $"{pane} opens its page with its title: {heading}");
            await Task.Delay(300, cancellation);
            await run.ShotAsync(window, "pane-" + pane.ToString().ToLowerInvariant());
        }

        // Choosing a pane in the navigation (what a click does) opens it.
        window.Navigation.SelectedItem = items[(int)SettingsPane.Calendars];
        run.Check(await WaitFor(() => window.Shell.SelectedPane == SettingsPane.Calendars), "choosing a pane in the navigation opens it");
        window.Shell.SelectedPane = SettingsPane.General;
        await WaitFor(() => window.CurrentPage is GeneralPage, 4000);
        await Task.Delay(300, cancellation);

        // ---- 3. General's hub drills into About and back ---------------------------------------------------------------------
        var general = window.CurrentPage as GeneralPage;
        run.Check(general is { HubPanel.Visibility: Visibility.Visible, Back.Visibility: Visibility.Collapsed }, "the General hub shows its rows and no back button");
        if (general is not null)
        {
            var aboutCard = general.HubPanel.Children.OfType<SettingsCard>().First();
            var clicked = ClickCard(window, aboutCard);
            run.Check(clicked, "a real mouse click is delivered on the About row (a settings card)");
            var drilled = await WaitFor(() => window.Shell.Destination == GeneralDestination.About, 2500);
            if (!drilled)
            {
                // Some hosted runners never let a click reach a window that is not in front; the drill-down itself is checked through the shell.
                run.Info($"the mouse click did not reach the About row on this machine (foreground window: {TimeTug.Platform.Tug.TugWindowChrome.IsForeground(window.Handle)}); the row was opened through the shell instead");
                window.Shell.OpenDestination(GeneralDestination.About);
            }

            run.Check(await WaitFor(() => window.Shell.Destination == GeneralDestination.About), "choosing About drills into it");
            await WaitFor(() => general.Back.Visibility == Visibility.Visible && general.Title.Text == "About" && general.HubPanel.Visibility == Visibility.Collapsed, 4000);
            run.Check(general.Back.Visibility == Visibility.Visible && general.Title.Text == "About" && general.HubPanel.Visibility == Visibility.Collapsed, "About shows its title and a back button");
            await run.ShotAsync(window, "about");
            run.Check(ScenarioUi.Invoke(window, "Back to General"), "the back button is named and can be pressed");
            run.Check(await WaitFor(() => window.Shell.Destination is null), "back returns to the hub");
            run.Check(await WaitFor(() => general.HubPanel.Visibility == Visibility.Visible, 4000), "the hub shows again");
        }

        // ---- 4. search ----------------------------------------------------------------------------------------------------
        window.TypeInSearch("lead");
        var suggestions = window.SearchBox.ItemsSource as IReadOnlyList<SettingsSearchResult>;
        run.Check(suggestions is { Count: 1 } && suggestions[0].Item.Id == "lead-time" && suggestions[0].PaneTitle == "Tug Rules", "'lead' finds Lead time in Tug Rules");
        window.Choose(suggestions![0]);
        run.Check(window.Shell.SelectedPane == SettingsPane.TugRules && window.Shell.HighlightedId == "lead-time", "choosing it opens Tug Rules and highlights Lead time");
        window.TypeInSearch("zoom");
        run.Check(((IReadOnlyList<SettingsSearchResult>?)window.SearchBox.ItemsSource)?.Any(r => r.Item.Id == "video-link") == true, "a keyword ('zoom') finds Require a video link");
        window.TypeInSearch("cafe");
        suggestions = window.SearchBox.ItemsSource as IReadOnlyList<SettingsSearchResult>;
        run.Check(suggestions is { Count: 1 } && suggestions[0].Item.Id == "calendar:google-conn-1/cafe", "'cafe' finds the calendar 'Café Sync' (case and accents ignored)");
        window.Choose(suggestions![0]);
        run.Check(window.Shell.SelectedPane == SettingsPane.Calendars, "choosing a calendar opens the Calendars pane");
        window.TypeInSearch("holiday");
        run.Check(((IReadOnlyList<SettingsSearchResult>?)window.SearchBox.ItemsSource)?.Any(r => r.Item.Id == "calendar:google-conn-1/holidays") == true, "a subscribed or system calendar is found too");
        window.TypeInSearch("zzzz");
        suggestions = window.SearchBox.ItemsSource as IReadOnlyList<SettingsSearchResult>;
        run.Check(suggestions is [{ Title: "No results" }], "nothing found offers 'No results'");
        var before = (window.Shell.SelectedPane, window.Shell.HighlightedId);
        window.Choose(suggestions![0]);
        run.Check((window.Shell.SelectedPane, window.Shell.HighlightedId) == before, "choosing 'No results' does nothing");
        window.TypeInSearch("update");
        run.Check(((IReadOnlyList<SettingsSearchResult>?)window.SearchBox.ItemsSource)?.Any(r => r.Title.Contains("Update", StringComparison.OrdinalIgnoreCase)) != true, "no Software Update entry is offered");
        window.TypeInSearch("version");
        window.SearchBox.IsSuggestionListOpen = true;
        await Task.Delay(600, cancellation);
        await run.ShotAsync(window, "search-results");

        // The About entry opens the About page and tints its title for about a second and a half.
        var revealed = System.Diagnostics.Stopwatch.StartNew();
        window.Choose(((IReadOnlyList<SettingsSearchResult>)window.SearchBox.ItemsSource!).First(r => r.Item.Id == "about"));
        run.Check(window.Shell.SelectedPane == SettingsPane.General && window.Shell.Destination == GeneralDestination.About, "the About entry drills into About");
        // The tint is a timed effect (it fades in over a quarter of a second, stays until 1.5 s after the reveal, fades out), so it is
        // watched: the opacity is sampled every 20 ms for up to 5 s, each sample stamped with the time since the reveal, and the three
        // statements below are read off those samples. A slow runner delays the samples; it does not move a one-shot read past the end
        // of the tint.
        var samples = await SampleTintAsync(window, revealed, 5000, cancellation);
        var overlay = samples.Overlay;
        run.Check(overlay is not null && samples.Points.Any(p => p.Opacity > 0.01), "the revealed control is tinted");
        var mid = samples.Points.Where(p => p.Elapsed >= 450 && p.Elapsed <= 1250).ToList();
        run.Check(mid.Count > 0 && mid.All(p => p.Opacity > 0.01), $"the tint is still there half a second later ({mid.Count} samples between 0.45 s and 1.25 s, lowest {(mid.Count == 0 ? double.NaN : mid.Min(p => p.Opacity)):0.000})");
        await run.ShotAsync(window, "highlight");
        run.Check(await WaitFor(() => window.Shell.HighlightedId is null, 3000), "the highlight ends by itself");
        run.Check(await WaitFor(() => overlay is { Opacity: < 0.01 }, 3000), "the tint is gone after about 1.5 seconds: " + overlay?.Opacity);

        // Reduced motion: the tint appears at full strength at once.
        ui.AnimationsEnabled = false;
        window.Shell.Open(SettingsPane.General, "about", GeneralDestination.About);
        overlay = await FirstOverlayAsync(window, WaitFor);
        await WaitFor(() => overlay is { } o && Math.Abs(o.Opacity - SettingsHighlight.Strength) < 0.0001 || window.Shell.HighlightedId is null, 1400);
        run.Check(overlay is { } o && Math.Abs(o.Opacity - SettingsHighlight.Strength) < 0.0001, "with reduced motion the tint is at full strength at once: " + overlay?.Opacity);
        run.Check(await WaitFor(() => window.Shell.HighlightedId is null, 3000), "and it ends the same way");
        ui.AnimationsEnabled = true;
        window.TypeInSearch(string.Empty);
        run.Check(window.SearchBox.ItemsSource is null, "clearing the search clears the results");

        // ---- 5. the flyout's gear -----------------------------------------------------------------------------------------
        settings.Close();
        run.Check(await WaitFor(() => !settings.IsOpen), "the Settings window closes");
        tray.PrimaryClick();
        run.Check(await WaitFor(() => flyout.IsOpen), "the flyout opens");
        var factoryFlyout = shell.Services.GetRequiredService<IFlyoutSurfaceFactory>() as FlyoutSurfaceFactory;
        var flyoutWindow = factoryFlyout?.Last?.Window;
        if (flyoutWindow is not null)
        {
            await SelfTestRunner.WaitForFrameAsync(flyoutWindow, cancellation, 700);
            run.Check(ScenarioUi.Invoke(flyoutWindow, "Settings"), "the gear in the flyout's header is pressed");
            run.Check(await WaitFor(() => settings.IsOpen && !flyout.IsOpen), "the gear closes the flyout and opens the Settings window");
            run.Check(factory.Created == 2, "a new Settings window was made (the first had been closed): " + factory.Created);
            flyoutWindow = null;
        }

        // ---- 5b. the other doors into Settings: a problem row on the flyout, and "Add another" on the welcome window ---------------
        // Each goes through the app shell's own wiring to the one Settings window, which moves to the Accounts pane (once, no second window).
        window = factory.Last!.Window;
        settings.Show(SettingsPane.TugRules);
        run.Check(await WaitFor(() => window.Shell.SelectedPane == SettingsPane.TugRules), "the open Settings window is on Tug Rules");
        engine.Push(SettingsRig.State(DateTimeOffset.UtcNow) with
        {
            Problems = [new SourceProblem("google-conn-1", "you@example.com", ProblemKind.Failing, "the server did not answer", "conn-1")],
        });
        tray.PrimaryClick();
        run.Check(await WaitFor(() => flyout.IsOpen), "the flyout opens with a problem row");
        flyoutWindow = factoryFlyout?.Last?.Window;
        if (flyoutWindow is not null)
        {
            await SelfTestRunner.WaitForFrameAsync(flyoutWindow, cancellation, 700);
            await WaitFor(() => flyoutWindow.ProblemRows.Count == 1, 4000);
            run.Check(ScenarioUi.Invoke(flyoutWindow, "Open Accounts to look at you@example.com"), "Open Accounts is pressed on the problem row");
            run.Check(
                await WaitFor(() => !flyout.IsOpen && window.Shell.SelectedPane == SettingsPane.Accounts && window.Shell.HighlightedId == TimeTug.Presentation.Welcome.AccountsViewModel.Ids.AccountPrefix + "conn-1"),
                $"the flyout closes and the same Settings window is on Accounts with that account tinted: {window.Shell.SelectedPane}, {window.Shell.HighlightedId}");
            run.Check(factory.Created == 2 && settings.IsOpen, "no second Settings window was made: " + factory.Created);
            flyoutWindow = null;
        }

        settings.Show(SettingsPane.TugRules);
        run.Check(await WaitFor(() => window.Shell.SelectedPane == SettingsPane.TugRules), "the Settings window is on Tug Rules again");
        shell.ShowWelcome();
        run.Check(await WaitFor(() => shell.Welcome is not null), "the welcome window opens");
        if (shell.Welcome is { } welcome)
        {
            await welcome.ViewModel.AddAnotherAsync();
            run.Check(await WaitFor(() => window.Shell.SelectedPane == SettingsPane.Accounts && shell.Welcome is null), "Add another closes the welcome window and opens Settings on Accounts");
            run.Check(factory.Created == 2 && settings.IsOpen, "still one Settings window: " + factory.Created);
        }

        engine.Push(SettingsRig.State(DateTimeOffset.UtcNow));

        // ---- 6. where the window was left ---------------------------------------------------------------------------------------
        window = factory.Last!.Window;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation, 600);
        var target = new RectInt32(bounds.X + 40, bounds.Y + 30, Math.Max(window.AppWindow.Size.Width - 60, 800), Math.Max(window.AppWindow.Size.Height - 40, 560));
        window.AppWindow.MoveAndResize(target);
        await WaitFor(() => store.Load().SettingsWindow is { } now && Near(now.X, target.X) && Near(now.Y, target.Y) && Near(now.Width, target.Width) && Near(now.Height, target.Height), 6000);
        var saved = store.Load().SettingsWindow;
        run.Check(saved is { } s && Near(s.X, target.X) && Near(s.Y, target.Y) && Near(s.Width, target.Width) && Near(s.Height, target.Height), $"the place the person left it is kept: {saved} for {target.X},{target.Y},{target.Width},{target.Height}");
        settings.Close();
        await WaitFor(() => !settings.IsOpen);
        settings.Show();
        await WaitFor(() => settings.IsOpen);
        window = factory.Last!.Window;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation, 600);
        run.Check(Near(window.AppWindow.Position.X, target.X) && Near(window.AppWindow.Position.Y, target.Y) && Near(window.AppWindow.Size.Width, target.Width) && Near(window.AppWindow.Size.Height, target.Height),
            $"it comes back where it was: {window.AppWindow.Position.X},{window.AppWindow.Position.Y} {window.AppWindow.Size.Width}x{window.AppWindow.Size.Height}");
        settings.Close();
        await WaitFor(() => !settings.IsOpen);
        store.Save(store.Load() with { SettingsWindow = new WindowGeometry(40000, 30000, 900, 600) });
        settings.Show();
        await WaitFor(() => settings.IsOpen);
        window = factory.Last!.Window;
        await SelfTestRunner.WaitForFrameAsync(window, cancellation, 600);
        var restored = new PixelRect(window.AppWindow.Position.X, window.AppWindow.Position.Y, window.AppWindow.Size.Width, 48);
        run.Check(monitors.Monitors.Any(m => Overlap(restored, m.Work) >= 120 * 48 / 2), $"a remembered place on a display that is gone is discarded: {restored.X},{restored.Y}");

        // ---- 7. every control is named; the Mica window has no unnamed control -------------------------------------------------
        window.Shell.SelectedPane = SettingsPane.General;
        await WaitFor(() => window.CurrentPage is GeneralPage, 4000);
        await Task.Delay(300, cancellation);
        run.Check(ScenarioUi.Unnamed(window).Count == 0, "every control has a name: " + string.Join(", ", ScenarioUi.Unnamed(window)));

        // ---- 8. the toolkit's settings controls under this Windows App SDK -------------------------------------------------------
        await ProbeSettingsControlsAsync(run, cancellation);

        // ---- 9. quitting closes the window ---------------------------------------------------------------------------------------
        await shell.QuitAsync(exitProcess: false).WaitAsync(TimeSpan.FromSeconds(30), cancellation);
        run.Check(!settings.IsOpen, "quitting closed the Settings window");
        run.Check(diagnostics.Entries.All(e => e.Level != "error" || e.Category == "tray"), "nothing was recorded as an error (a tray that could not be created aside): "
            + string.Join("; ", diagnostics.Entries.Where(e => e.Level == "error").Select(e => e.Category + ": " + e.Message)));
        return run.Finish();
    }

    private static async Task ProbeSettingsControlsAsync(ScenarioRun run, CancellationToken cancellation)
    {
        var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off" };
        AutomationProperties.SetName(toggle, "Probe switch");
        var combo = new ComboBox { ItemsSource = new[] { "Use system", "Light", "Dark" }, SelectedIndex = 0 };
        AutomationProperties.SetName(combo, "Probe choice");
        var card = new SettingsCard { Header = "Probe card", Description = "A settings card with a switch.", HeaderIcon = new FontIcon { Glyph = "" }, Content = toggle };
        var inner = new SettingsCard { Header = "Probe item", Content = combo };
        var expander = new SettingsExpander
        {
            Header = "Probe expander",
            Description = "A settings expander with one item.",
            HeaderIcon = new FontIcon { Glyph = "" },
            IsExpanded = true,
        };
        expander.Items.Add(inner);
        var panel = new StackPanel { Spacing = 4, Padding = new Thickness(24) };
        panel.Children.Add(card);
        panel.Children.Add(expander);
        var probe = new Window { Title = "Settings controls probe", Content = panel };
        probe.Activate();
        await SelfTestRunner.WaitForFrameAsync(probe, cancellation, 1200);
        run.Check(card.ActualHeight > 30 && card.ActualWidth > 100, $"SettingsCard lays out: {card.ActualWidth:0}x{card.ActualHeight:0}");
        run.Check(expander.ActualHeight > card.ActualHeight && inner.ActualHeight > 20, $"SettingsExpander lays out expanded with its item: {expander.ActualWidth:0}x{expander.ActualHeight:0}, item {inner.ActualHeight:0}");
        var unnamed = FrameworkElementAutomationPeer.CreatePeerForElement(card)?.GetName() ?? string.Empty;
        run.Info("SettingsCard automation name with only a Header set: '" + unnamed + "'");
        AutomationProperties.SetName(card, "Probe card");
        run.Check(FrameworkElementAutomationPeer.CreatePeerForElement(card)?.GetName() == "Probe card", "SettingsCard takes an explicit automation name");
        var expandedHeight = expander.ActualHeight;
        expander.IsExpanded = false;
        await PollAsync(() => expander.ActualHeight < expandedHeight, 4000, cancellation);
        var collapsed = expander.ActualHeight;
        expander.IsExpanded = true;
        await PollAsync(() => expander.ActualHeight > collapsed, 4000, cancellation);
        run.Check(expander.ActualHeight > collapsed, $"SettingsExpander collapses and expands: {collapsed:0} -> {expander.ActualHeight:0}");
        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(expander);
        run.Info("SettingsExpander name with only a Header set: '" + peer?.GetName() + "'");
        run.Info("SettingsExpander automation peer: " + peer?.GetType().Name + ", pattern ExpandCollapse: " + (peer?.GetPattern(PatternInterface.ExpandCollapse) is not null));
        peer = FrameworkElementAutomationPeer.CreatePeerForElement(card);
        run.Info("SettingsCard automation peer: " + peer?.GetType().Name + ", Invoke pattern: " + (peer?.GetPattern(PatternInterface.Invoke) is not null));
        await run.ShotAsync(probe, "settings-controls-probe");
        probe.Close();
    }

    private static string HeadingOf(FrameworkElement? page) => FindHeading(page)?.Text ?? string.Empty;

    private static TextBlock? FindHeading(DependencyObject? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is TextBlock { Name: "Heading" } heading)
        {
            return heading;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (FindHeading(VisualTreeHelper.GetChild(node, i)) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>The first highlight halo that has started to show, polled until it does.</summary>
    private static async Task<Border?> FirstOverlayAsync(SettingsWindow window, Func<Func<bool>, int, Task<bool>> waitFor)
    {
        Border? found = null;
        await waitFor(() =>
        {
            found = window.CurrentPage is { } page ? SettingsHighlight.Hosts(page).Select(SettingsHighlight.OverlayOf).FirstOrDefault(o => o is { Opacity: > 0 }) : null;
            return found is not null;
        }, 2000);
        return found;
    }

    /// <summary>Polls until the condition holds or the wall-clock time is up.</summary>
    private static async Task<bool> PollAsync(Func<bool> condition, int milliseconds, CancellationToken cancellation)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25, cancellation);
        }

        return condition();
    }

    /// <summary>The tint's opacity over time since the reveal: the halo that was found and every sample taken (time in ms, opacity).</summary>
    private sealed record TintSamples(Border? Overlay, IReadOnlyList<(long Elapsed, double Opacity)> Points);

    private static async Task<TintSamples> SampleTintAsync(SettingsWindow window, System.Diagnostics.Stopwatch sinceReveal, int milliseconds, CancellationToken cancellation)
    {
        Border? found = null;
        var points = new List<(long Elapsed, double Opacity)>();
        var stop = sinceReveal.ElapsedMilliseconds + milliseconds;
        while (sinceReveal.ElapsedMilliseconds < stop)
        {
            found ??= window.CurrentPage is { } page
                ? SettingsHighlight.Hosts(page).Select(SettingsHighlight.OverlayOf).FirstOrDefault(o => o is { Opacity: > 0 })
                : null;
            if (found is not null)
            {
                points.Add((sinceReveal.ElapsedMilliseconds, found.Opacity));
                if (sinceReveal.ElapsedMilliseconds > 1800 && found.Opacity < 0.01)
                {
                    break;
                }
            }

            await Task.Delay(20, cancellation);
        }

        return new TintSamples(found, points);
    }

    /// <summary>A real mouse click in the middle of the card (a settings card has no UI Automation Invoke pattern).</summary>
    private static bool ClickCard(SettingsWindow window, SettingsCard card)
    {
        var scale = window.RootElement.XamlRoot.RasterizationScale;
        var centre = card.TransformToVisual(window.RootElement).TransformPoint(new Windows.Foundation.Point(card.ActualWidth / 2, card.ActualHeight / 2));
        var side = (window.AppWindow.Size.Width - window.AppWindow.ClientSize.Width) / 2;
        var top = window.AppWindow.Size.Height - window.AppWindow.ClientSize.Height - side;
        return MouseInput.ClickAt(
            window.AppWindow.Position.X + side + (int)(centre.X * scale), window.AppWindow.Position.Y + top + (int)(centre.Y * scale));
    }

    private static bool Near(int actual, int expected) => Math.Abs(actual - expected) <= 3;

    private static int Overlap(PixelRect a, PixelRect b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
        return width > 0 && height > 0 ? width * height : 0;
    }

    private sealed class NoHotkeyStore : IHotkeyStore
    {
        public HotkeyState Load() => HotkeyState.None;

        public void Save(HotkeyState state)
        {
        }
    }
}
#endif
