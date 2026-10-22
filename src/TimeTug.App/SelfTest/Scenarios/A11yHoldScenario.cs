#if SELFTEST
using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TimeTug.App.Settings;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>a11y-hold</c>: shows the welcome window and the tug (the main window, on one monitor) and holds them open while
/// the accessibility scanner (<c>tests/TimeTug.App.A11y</c>, axe-windows) reads them. Round 0 is a canary: a window with a button that
/// has no name, which the scanner must flag (a scan that finds nothing there is not seeing the app, and fails). Then three rounds: the
/// welcome window's Connect step with the tug as it first appears, then its Connected step with the tug showing the link to copy after a
/// failed Join, then the flyout (a problem row and a long day), then the Settings window (its search results, then the Tug Rules, Calendars, Accounts and General panes and the About page). A round starts by writing <c>ready-N.json</c> (the process id) into the output folder and ends when the scanner writes
/// <c>done-N.txt</c>; without it the scenario gives up after <see cref="WaitForScanner"/>.
/// </summary>
internal static class A11yHoldScenario
{
    public const string Name = "a11y-hold";

    /// <summary>How many rounds the scanner is told to expect (<c>scripts/ci/run-a11y.ps1</c> passes the same number).</summary>
    public const int Rounds = 9;

    private static readonly TimeSpan WaitForScanner = TimeSpan.FromSeconds(120);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        using var rig = TugRig.Create(options, 1, cancellation);
        var run = rig.Run;

        // Round 0: the scanner has to see a violation that is certainly there.
        var canary = new Window { Title = "Accessibility canary", Content = new Button { Content = new FontIcon { Glyph = "\uE713" } } };
        canary.Activate();
        await SelfTestRunner.WaitForFrameAsync(canary, cancellation, 800);
        run.Check(await HoldForScannerAsync(rig, 0, "canary: a button with no name", cancellation), "the scanner finished the canary round");
        canary.Close();

        rig.Engine.Push(Scenes.Empty());
        // The Connected step carries the launch-at-login checkbox: scanned while Windows blocks it (the checkbox disabled with its note and link).
        var welcomeStartup = new ScriptedStartupTask { State = StartupState.DisabledByUser };
        var welcomeLaunch = new LaunchAtLoginViewModel(
            welcomeStartup, new RecordingSystemLauncher(), new DispatcherQueueDispatcher(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()), rig.Text);
        var welcome = WelcomeWindowFactory.Create(rig.Engine, rig.Forms, rig.Text, rig.Time, launchAtLogin: welcomeLaunch);
        welcome.Activate();
        await SelfTestRunner.WaitForFrameAsync(welcome, cancellation);
        run.Check(welcome.ViewModel.Step == WelcomeStep.Connect, "the welcome window shows the Connect step");

        run.Check(await rig.PresentAsync(rig.Request("e-a11y", "Design review", TimeSpan.FromSeconds(240))), "the tug opens");
        await rig.Settle();
        run.Check(await HoldForScannerAsync(rig, 1, "Connect step and the tug", cancellation), "the scanner finished round 1");

        // Round 2: the other step of the welcome window, and the tug with the meeting link to copy.
        run.Check(ScenarioUi.Invoke(welcome, "Connect Google"), "Connect Google is clicked, so the next account moves the window on");
        await rig.Settle(500);
        rig.Engine.Push(Scenes.Connected(rig.Time.GetUtcNow()));
        rig.Launcher.Succeeds = false;
        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Join meeting"), "Join is clicked (and fails)");
        run.Check(await rig.WaitFor(() => rig.Host.Primary?.ViewModel.HasJoinFailed == true), "the tug shows the link to copy");
        run.Check(await rig.WaitFor(() => welcome.ViewModel.Step == WelcomeStep.Connected), "the welcome window moved to the Connected step");
        await rig.Settle();
        run.Check(await HoldForScannerAsync(rig, 2, "Connected step and the link to copy", cancellation), "the scanner finished round 2");

        run.Check(ScenarioUi.Invoke(rig.Host.Primary!, "Dismiss this reminder"), "Dismiss can be pressed");
        run.Check(await rig.WaitHiddenAsync(), "the tug closes");
        welcome.Close();

        // Round 3: the flyout, with a problem row above a day that has every kind of card (and the scrolling list).
        using var flyout = FlyoutRig.Over(rig);
        rig.Engine.Push(flyout.State(flyout.LongDay(), [new SourceProblem("google-conn-1", "you@example.com", ProblemKind.AuthExpired, null, "conn-1")]));
        var flyoutWindow = await flyout.OpenAsync();
        run.Check(flyoutWindow is not null, "the flyout opens");
        await rig.Settle();
        run.Check(await HoldForScannerAsync(rig, 3, "the flyout with a problem and a long day", cancellation), "the scanner finished round 3");
        flyout.Host.Close();

        // Round 4: the Settings window on its General pane, the search box with a result list.
        using var settings = SettingsRig.Over(rig);
        rig.Engine.Push(SettingsRig.State(rig.Time.GetUtcNow()));
        var settingsWindow = await settings.OpenAsync();
        run.Check(settingsWindow is not null, "the Settings window opens");
        if (settingsWindow is not null)
        {
            settingsWindow.TypeInSearch("lead");
            await rig.Settle();
            run.Check(await HoldForScannerAsync(rig, 4, "the Settings window with the search results open", cancellation), "the scanner finished round 4");

            // Round 5: the Tug Rules pane, with both warnings showing.
            settingsWindow.TypeInSearch(string.Empty);
            rig.Engine.Push(SettingsRig.State(rig.Time.GetUtcNow(), tugRequirements: true));
            settingsWindow.Shell.SelectedPane = SettingsPane.TugRules;
            await rig.Settle();
            run.Check(settingsWindow.CurrentPage is TugRulesPage, "the Tug Rules pane is showing");
            run.Check(await HoldForScannerAsync(rig, 5, "the Tug Rules pane with its warnings", cancellation), "the scanner finished round 5");

            // Round 6: the Calendars pane (a hidden calendar with its Tug switch disabled, the subscribed list open, the options).
            settingsWindow.Shell.SelectedPane = SettingsPane.Calendars;
            await rig.Settle();
            if (settingsWindow.CurrentPage is CalendarsPage calendars && calendars.Disclosure is { } disclosure)
            {
                disclosure.IsExpanded = true;
            }

            await rig.Settle();
            run.Check(settingsWindow.CurrentPage is CalendarsPage, "the Calendars pane is showing");
            run.Check(await HoldForScannerAsync(rig, 6, "the Calendars pane", cancellation), "the scanner finished round 6");

            // Round 7: the Accounts pane (an account in every status, the Add cards with one not set up in this build, the notice, Diagnostics).
            rig.Engine.Push(SettingsRig.AccountsState(rig.Time.GetUtcNow()));
            settingsWindow.Shell.SelectedPane = SettingsPane.Accounts;
            await rig.Settle();
            run.Check(settingsWindow.CurrentPage is AccountsPage { AccountCards.Count: 7 }, "the Accounts pane is showing the accounts");
            run.Check(await HoldForScannerAsync(rig, 7, "the Accounts pane with an account in every status", cancellation), "the scanner finished round 7");

            // Round 8: the General pane (launch at login turned off by the person, the shortcut recorder reporting a conflict).
            settings.Startup.State = StartupState.DisabledByUser;
            ((ScriptedHotkeys)settings.Hotkeys).Show(new HotkeyState(new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T'), HotkeyStatus.Conflict, rig.Time.GetUtcNow(), new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, 'T')));
            settingsWindow.Shell.SelectedPane = SettingsPane.General;
            await rig.Settle();
            run.Check(settingsWindow.CurrentPage is GeneralPage { LaunchToggle.IsEnabled: false }, "the General pane is showing launch at login as blocked");
            run.Check(await HoldForScannerAsync(rig, 8, "the General pane with a blocked launch at login and a shortcut conflict", cancellation), "the scanner finished round 8");

            // Round 9: the About page (the hero picture, the lockup, the links).
            settingsWindow.Shell.OpenDestination(GeneralDestination.About);
            await rig.Settle();
            run.Check(settingsWindow.CurrentPage is GeneralPage { AboutContent.Visibility: Visibility.Visible }, "the About page is showing");
            run.Check(await HoldForScannerAsync(rig, 9, "the About page", cancellation), "the scanner finished round 9");
            settings.Host.Close();
        }

        return rig.Finish();
    }

    private static async Task<bool> HoldForScannerAsync(TugRig rig, int round, string what, CancellationToken cancellation)
    {
        var folder = rig.Run.Folder;
        var ready = JsonSerializer.Serialize(new { round, what, processId = Environment.ProcessId, started = Process.GetCurrentProcess().StartTime.ToUniversalTime() });
        await File.WriteAllTextAsync(Path.Combine(folder, $"ready-{round}.json"), ready, cancellation);
        var done = Path.Combine(folder, $"done-{round}.txt");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(WaitForScanner);
        try
        {
            while (!File.Exists(done))
            {
                await Task.Delay(250, timeout.Token);
            }

            return true;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return false;
        }
    }
}
#endif
