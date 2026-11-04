#if SELFTEST
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.Platform.Capture;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>welcome</c>: opens the welcome window against a scripted engine and walks all four steps: Welcome, Connect your calendars (a
/// browser sign-in and its cancel, the iCloud form, a failed sign-in), You're connected (the calendars with their Tug switches, an account that needs the person, an account
/// added meanwhile, Test tug) and Keep TimeTug handy (start at sign-in, the taskbar card and its guide), saving a screenshot at each stop.
/// Controls are clicked through UI Automation, the way Narrator or a keyboard user would, so names and bindings are exercised too.
/// </summary>
internal static class WelcomeScenario
{
    public const string Name = "welcome";

    private static readonly DateTimeOffset Noon = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    public static async Task<SelfTestResult> RunAsync(SelfTestOptions options, CancellationToken cancellation)
    {
        var run = new ScenarioRun(options);
        void Check(bool condition, string description) => run.Check(condition, description);

        var queue = DispatcherQueue.GetForCurrentThread();
        var text = new ResourceLoaderLocalizer();
        using var forms = new AccountFormService(queue, text);
        // The engine takes a settings patch the way the real one does, so the calendars' switches can be seen to change.
        using var engine = new ScriptedEngineClient(forms) { ShowFormFor = "icloud", ReflectSettings = true };
        using var settingsClient = new SettingsClient(engine);
        engine.Push(Scenes.Empty());
        var startup = new ScriptedStartupTask();
        var links = new RecordingSystemLauncher();
        var launch = new LaunchAtLoginViewModel(startup, links, new DispatcherQueueDispatcher(queue), text);
        var diagnostics = new RecordingDiagnostics();
        var taskbar = new TaskbarSettingsViewModel(links, new DispatcherQueueDispatcher(queue), text, diagnostics);
        var uiSettings = new FakeUiSettings();
        var window = WelcomeWindowFactory.Create(engine, forms, text, new FixedClock(Noon), launchAtLogin: launch, taskbar: taskbar, uiSettings: uiSettings, settings: settingsClient);
        var model = window.ViewModel;
        window.Activate();
        await SelfTestRunner.WaitForFrameAsync(window, cancellation);

        Task Shot(string label) => run.ShotAsync(window, label);

        async Task Settle(int milliseconds = 800) => await Task.Delay(milliseconds, cancellation);

        Task<bool> WaitFor(Func<bool> condition, int milliseconds) => WaitUntilAsync(condition, milliseconds, cancellation);
        var box = window.AccountsList;

        // Step 1: "Welcome to TimeTug" with Get started. Nothing else is on screen.
        Check(model.Step == WelcomeStep.Intro, "starts on the welcome step");
        Check(!model.ShowLaunchAtLogin && ((FrameworkElement)window.LaunchCheckBox.Parent).Visibility == Visibility.Collapsed, "the launch-at-login checkbox is not offered on the first step");
        Check(startup.Calls.Count == 0 && window.GuideAsset is null, "nothing for the last step has run: Windows was not asked about the startup task and the guide is not loaded");
        Check(window.GetStarted.Visibility == Visibility.Visible && ((FrameworkElement)box.Parent).Visibility == Visibility.Collapsed, "the intro offers Get started and not the accounts list yet");
        Check(Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("intro");
        Check(Invoke(window, "Get started"), "Get started can be invoked by name");
        await Settle(400);
        Check(model.Step == WelcomeStep.Connect, "Get started moves to Connect your calendars");

        // Step 2: the accounts control. The scripted engine knows no Windows account, so nothing is offered; the box says it is empty.
        Check(!model.Accounts.WindowsAccountOffered, "nothing is offered to Continue: Windows lists no account here");
        Check(box.EmptyLineText.Visibility == Visibility.Visible && box.EmptyLineText.Text == "No accounts yet. Choose + to add one.", "the empty box says how to add: " + box.EmptyLineText.Text);
        Check(!window.Continue.IsEnabled && window.Continue.Visibility == Visibility.Visible, "Continue is disabled: nothing to continue to");
        Check(box.RemoveMinus is { IsEnabled: false } && AutomationProperties.GetName(box.RemoveMinus) == "Remove account", "- is disabled and named Remove account");
        Check(AutomationProperties.GetName(box.AddPlus) == "Add account" && box.AddPlus.IsEnabled, "+ is named Add account and is enabled");
        Check(Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("connect");

        // The + menu lists what the build can sign in to, Microsoft and Google first; a kind it cannot is disabled with the reason.
        var items = await AccountsListUi.OpenMenuAsync(WaitFor, box);
        Check(items.Select(i => i.Text).SequenceEqual(["Microsoft", "Google", "iCloud", "Other (CalDAV)"]), "the + menu: " + string.Join(", ", items.Select(i => i.Text)));
        Check(items.Count > 0 && !items[0].IsEnabled && items[0].KeyboardAcceleratorTextOverride.Contains("isn’t set up", StringComparison.Ordinal), "Microsoft is disabled with the reason");
        Check(items.Skip(1).All(i => i.IsEnabled), "the others can be chosen");
        await Shot("menu");
        Check(await AccountsListUi.CloseMenuAsync(WaitFor, box), "the menu closes without choosing");

        // A browser sign-in shows its progress and can be cancelled.
        Check(await AccountsListUi.ChooseAsync(WaitFor, box, "Connect Google"), "Google is chosen from the + menu");
        await Settle();
        Check(engine.Sent.Contains("add:google"), "Google sends account.add");
        Check(model.Accounts.IsBusy && !box.AddPlus.IsEnabled && !box.RemoveMinus.IsEnabled, "while signing in + and - are disabled");
        Check(box.WaitingPanel.Visibility == Visibility.Visible && box.WaitingLabel.Text == "Waiting for your browser…", "the waiting row shows: " + box.WaitingLabel.Text);
        await Shot("waiting");
        Check(Invoke(window, "Cancel signing in"), "the Cancel button can be invoked by name");
        await Settle(400);
        Check(engine.Sent.Contains("cancel") && !model.Accounts.IsBusy, "Cancel sends account.cancel and ends the wait");

        // iCloud asks for its details in a dialog built from the engine's field descriptions.
        Check(await AccountsListUi.ChooseAsync(WaitFor, box, "Connect iCloud"), "iCloud is chosen from the + menu");
        for (var i = 0; i < 20 && VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot).Count == 0; i++)
        {
            await Settle(250);
        }

        await Settle(600);
        Check(VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot).Count > 0, "the credential form is open");
        Check(Unnamed(window).Count == 0, "every control, dialog included, has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("form");
        await engine.CancelAuthorizationAsync();
        await Settle(600);
        Check(VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)window.Content).XamlRoot).Count == 0, "cancelling closes the form");

        // A failed sign-in stays on this step with the error under the box.
        engine.ShowFormFor = null;
        Check(await AccountsListUi.ChooseAsync(WaitFor, box, "Connect Other (CalDAV)"), "CalDAV is chosen from the + menu");
        engine.Push(Scenes.Empty(new AccountActivity(false, "Sign-in failed: the server rejected that user name or password.", "inApp")));
        Check(await WaitFor(() => box.ErrorLine.Text.Length > 0, 4000), "the failure shows under the box");
        Check(model.Step == WelcomeStep.Connect && box.ErrorLine.Text == "Couldn’t connect to Other (CalDAV). Check your details and try again.", "in words, keeping the step: " + box.ErrorLine.Text);
        Check(box.ErrorDetailLine.Text == "Sign-in failed: the server rejected that user name or password.", "with the engine's detail");
        await Shot("error");

        // An account added with + joins the list and the window stays on this step; only Continue moves on.
        Check(await AccountsListUi.ChooseAsync(WaitFor, box, "Connect Google"), "Google can be tried again");
        // The engine's settings agree with the calendars it lists: Work tugs, Café Sync does not, Family is hidden.
        var connectedState = SettingsRig.State(Noon);
        connectedState = connectedState with
        {
            Settings = connectedState.Settings with
            {
                TakeoverCalendarKeys = new HashSet<string>(["google-conn-1/work"], StringComparer.Ordinal),
                HiddenCalendarKeys = new HashSet<string>(["google-conn-1/family"], StringComparer.Ordinal),
            },
        };
        engine.Push(connectedState);
        Check(await WaitFor(() => model.Accounts.ListAccounts.Count == 1, 4000), "the new account is in the list");
        await Settle(600);
        Check(model.Step == WelcomeStep.Connect, "an account added with + does not move the window on");
        Check(AccountsListUi.ExposesListPatterns(box), "the list exposes the Selection pattern and its row the SelectionItem pattern" + ": " + AccountsListUi.ListPatternReport(box).Detail);
        Check(AccountsListUi.RowNames(box).SequenceEqual(["Google, you@example.com, Connected"]), "the row reads as provider, name and status: " + string.Join(" | ", AccountsListUi.RowNames(box)));
        Check(box.EmptyLineText.Visibility == Visibility.Collapsed && window.Continue.IsEnabled, "the empty line is gone and Continue is enabled");
        Check(Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("connect-with-account");

        // Step 3: you're connected. The calendars the engine found, each with its Tug switch: the list Settings > Calendars shows, saved as it is changed.
        Check(Invoke(window, "Continue"), "Continue can be invoked by name");
        await Settle(1000);
        Check(model.Step == WelcomeStep.Connected, "Continue moves to the Connected step");
        Check(model.Accounts.Accounts.Count == 1 && model.Accounts.Accounts[0].Summary.StartsWith("Google · you@example.com · 3 calendars", StringComparison.Ordinal), "the account is summarised");
        Check(Unnamed(window).Count == 0, "every control on the Connected step has an automation name: " + string.Join(", ", Unnamed(window)));
        var list = window.CalendarsList;
        Check(window.ConnectedStepPanel.Visibility == Visibility.Visible && window.HandyStepPanel.Visibility == Visibility.Collapsed && list.Visibility == Visibility.Visible, "step 3 shows the calendar list and not the last step");
        var step3 = Texts(window.ConnectedStepPanel);
        Check(step3.Contains("You’re connected") && step3.Contains("Choose which calendars are allowed to tug you away from your work.")
            && step3.Contains("Tug calendars") && step3.Contains("Tug") && step3.Contains("Google") && step3.Contains("· you@example.com · connected directly") && step3.Contains("All calendars") && step3.Contains("All")
            && step3.Contains("Work") && step3.Contains("Café Sync") && step3.Contains("Family"), "it has the heading, the line about the switches, the column titles, the account header, All and the calendars: " + string.Join(" | ", step3));
        Check(!step3.Any(t => t.Contains("Holidays", StringComparison.Ordinal) || t.Contains("Subscribed and system", StringComparison.Ordinal)) && list.Disclosure is null, "the subscribed and system calendars are left to Settings");
        Check(window.CalendarsEmptyText.Visibility == Visibility.Collapsed, "the empty line is not showing");
        Check(window.ConnectedContinue is { Visibility: Visibility.Visible, IsEnabled: true } && AutomationProperties.GetName(window.ConnectedContinue) == "Continue" && !window.Continue.IsEnabled, "the primary button reads Continue (Done is for the last step)");
        Check(window.Done.Visibility == Visibility.Collapsed && !model.ShowLaunchAtLogin && startup.Calls.Count == 0, "Done, the launch-at-login checkbox and the Windows startup task are not touched on this step");
        var work = list.ControlsOf("google-conn-1/work");
        var cafe = list.ControlsOf("google-conn-1/cafe");
        var family = list.ControlsOf("google-conn-1/family");
        var all = list.AllSwitchOf("google-conn-1", "you@example.com");
        Check(work is not null && cafe is not null && family is not null && all is not null, "every calendar has its check box and Tug switch, and the account its All switch");
        Check(work is { Show.IsChecked: true, Tug.IsOn: true } && cafe is { Show.IsChecked: true, Tug.IsOn: false } && family is { Show.IsChecked: false, Tug.IsEnabled: false } && all is { IsOn: false },
            "the engine's settings are shown as they are (Work tugs, Café Sync does not, Family is hidden so its switch is disabled)");
        Check(work is not null && AutomationProperties.GetName(work.Value.Show) == "Show Work in list" && AutomationProperties.GetName(work.Value.Tug) == "Tug for Work" && all is not null && AutomationProperties.GetName(all) == "Tug for every calendar in you@example.com",
            "each switch has a name Narrator can read: " + (work is null ? "-" : AutomationProperties.GetName(work.Value.Tug)));
        Check(list.ProblemOf("google-conn-1").Length == 0, "the account has no problem line");
        await Settle(900);   // a window capture lags a repaint
        await Shot("connected");

        // Every change is saved at once: Café Sync's switch, then the account's All switch, then hiding Work.
        var sent = engine.Sent.Count(c => c == "settings");
        Check(cafe is not null && ScenarioUi.Toggle(cafe.Value.Tug), "Café Sync's Tug switch can be turned on through UI Automation");
        Check(await WaitFor(() => engine.Sent.Count(c => c == "settings") == sent + 1 && model.Calendars!.Sections[0].Groups[0].Rows.Single(r => r.Key == "google-conn-1/cafe").Tugs, 4000), "it is saved at once (one settings patch) and the engine's state shows it");
        Check(cafe is { Tug.IsOn: true }, "and the switch stays on");
        Check(all is not null && ScenarioUi.Toggle(all), "the All switch can be turned on");
        Check(await WaitFor(() => model.Calendars!.Sections[0].Groups[0].Rows.All(r => r.IsShown && r.Tugs) && all is { IsOn: true }, 4000), "every calendar tugs and All stays on");
        Check(family is { Show.IsChecked: true, Tug.IsOn: true }, "All turns on every calendar of the account, the hidden one too, which it shows (as in Settings)");
        Check(work is not null && ScenarioUi.Toggle(work.Value.Show), "Work's check box can be cleared");
        Check(await WaitFor(() => work is { Show.IsChecked: false, Tug.IsEnabled: false, Tug.IsOn: false }, 4000), "hiding Work turns its Tug off and disables it (the same key lists as Settings)");
        Check(await WaitFor(() => model.Calendars!.Sections[0].Groups[0].AllTug is false && all is { IsOn: false }, 4000), "and All goes off because a calendar of the account no longer tugs");
        await Settle(900);   // a window capture lags a repaint
        await Shot("connected-tugging");

        // An account that needs the person says so under its own header.
        void Republish(Func<AppState, AppState> change)
        {
            var latest = engine.Latest!;
            engine.Push(change(latest) with { Revision = latest.Revision + 1 });
        }

        var well = connectedState.Accounts;
        Republish(state => state with { Accounts = [new AccountInfo("conn-1", "google", "you@example.com", "authExpired", 3)] });
        Check(await WaitFor(() => list.ProblemOf("google-conn-1") == "Sign in again", 4000), "an account that needs signing in again says so on its header: " + list.ProblemOf("google-conn-1"));
        Check(Texts(window.ConnectedStepPanel).Contains("Sign in again"), "and the words are on screen");
        await Settle(900);   // a window capture lags a repaint
        await Shot("connected-problem");

        // The list follows the accounts: another account arrives (it was added in Settings, say) with its calendars.
        var second = new CalendarGroup("work@example.com", "microsoft-conn-2", "microsoft", CalendarOrigin.Account, "work@example.com · connected directly", false,
            [new CalendarEntry("microsoft-conn-2/team", "Team", "#0078D4", true, false, "standard", false, false, "work@example.com")]) { Title = "Microsoft" };
        Republish(state => state with
        {
            Accounts = [.. well, new AccountInfo("conn-2", "microsoft", "work@example.com", "ok", 1)],
            CalendarGroups = [.. state.CalendarGroups, second],
        });
        Check(await WaitFor(() => list.ControlsOf("microsoft-conn-2/team") is not null && list.ProblemOf("google-conn-1").Length == 0, 4000), "a second account's calendars appear in the list without reopening the window, and the first account's line is gone");
        Check(Texts(window.ConnectedStepPanel).Contains("Microsoft") && list.AllSwitchOf("microsoft-conn-2", "work@example.com") is not null, "under its own header, with its own All switch");
        Republish(state => state with { Accounts = well, CalendarGroups = [.. state.CalendarGroups.Where(g => g.SourceId != "microsoft-conn-2")] });
        Check(await WaitFor(() => list.ControlsOf("microsoft-conn-2/team") is null, 4000), "and goes again when the account does");

        // Test tug still works from here, and so does the next meeting line.
        Check(Invoke(window, "Show a test tug screen") && engine.Sent.Contains("testTug"), "Test tug sends the test takeover");
        Check(model.Accounts.NextMeetingText.StartsWith("Next: Design review", StringComparison.Ordinal), "the next meeting line is there: " + model.Accounts.NextMeetingText);

        // A narrow window: the list shrinks to fit and nothing runs off the side.
        var wideStep3 = window.AppWindow.Size;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(440, wideStep3.Height));
        await Settle(800);
        var step3Width = ((FrameworkElement)window.Content).ActualWidth;
        Check(list.ActualWidth > 0 && list.ActualWidth <= step3Width && Texts(list).Contains("Work"), $"in a narrow window the list ({list.ActualWidth:0} px) fits the window ({step3Width:0} px)");
        await Shot("connected-narrow");
        window.AppWindow.Resize(wideStep3);
        await Settle(400);

        // Continue goes on to the last step; every choice above is saved already.
        Check(ScenarioUi.Invoke(window.ConnectedContinue), "Continue can be invoked");
        Check(await WaitFor(() => model.Step == WelcomeStep.Handy, 3000), "Continue moves to Keep TimeTug handy");
        Check(window.HandyStepPanel.Visibility == Visibility.Visible && window.ConnectedStepPanel.Visibility == Visibility.Collapsed && window.Done.Visibility == Visibility.Visible && window.ConnectedContinue.Visibility == Visibility.Collapsed,
            "step 4 shows its own panel and the Done button");
        Check(Texts(window.HandyStepPanel).Contains("Keep TimeTug handy") && Texts(window.HandyStepPanel).Any(t => t.StartsWith("Two small things", StringComparison.Ordinal)), "with its heading and line: " + string.Join(" | ", Texts(window.HandyStepPanel).Take(2)));
        Check(Unnamed(window).Count == 0, "every control on the last step has an automation name: " + string.Join(", ", Unnamed(window)));
        Check(AutomationProperties.GetName(window.Done) == "Done", "the last button reads Done");
        Check(engine.Sent.Count(c => c == "settings") == 3, "the window sent exactly the three patches the choices made: " + engine.Sent.Count(c => c == "settings"));
        await Settle(900);   // a window capture lags a repaint
        await Shot("handy");

        // Launch at login (step 4): unchecked until the person ticks it, and shown as Windows says after the tick.
        await Settle(400);
        Check(model.ShowLaunchAtLogin && ((FrameworkElement)window.LaunchCheckBox.Parent).Visibility == Visibility.Visible, "the last step offers the launch-at-login checkbox");
        Check(window.LaunchCheckBox.IsChecked == false && window.LaunchCheckBox.IsEnabled, "it starts unchecked");
        Check(AutomationProperties.GetName(window.LaunchCheckBox) == "Start TimeTug when I sign in" && window.LaunchCheckBox.Content is "Start TimeTug when I sign in", "and is named: " + AutomationProperties.GetName(window.LaunchCheckBox));
        Check(startup.Calls.SequenceEqual(["get"]) && startup.State == StartupState.Disabled, "Windows was only asked for the state; nothing was enabled");
        Check(ScenarioUi.Toggle(window.LaunchCheckBox), "the checkbox can be ticked through UI Automation");
        await Settle(500);
        Check(startup.Calls.SequenceEqual(["get", "enable"]) && window.LaunchCheckBox.IsChecked == true, "a tick requests enabling and the box stays ticked because Windows enabled it");
        await Shot("handy-launch-ticked");
        Check(ScenarioUi.Toggle(window.LaunchCheckBox), "unticking");
        await Settle(500);
        Check(startup.Calls.Last() == "disable" && window.LaunchCheckBox.IsChecked == false, "turns it off again");
        // The person has switched the task off in Windows: the tick is refused and the box says so, with the link.
        startup.StateAfterRequest = StartupState.DisabledByUser;
        Check(ScenarioUi.Toggle(window.LaunchCheckBox), "ticking while Windows is set to refuse");
        await Settle(500);
        Check(window.LaunchCheckBox.IsChecked == false && !window.LaunchCheckBox.IsEnabled, "the box goes back to unchecked and is disabled: Windows decides");
        Check(window.LaunchSettingsLink.Visibility == Visibility.Visible && window.LaunchNoteText.Text.Contains("Startup apps", StringComparison.Ordinal), "with the reason and the link: " + window.LaunchNoteText.Text);
        await Shot("handy-launch-blocked");
        Check(ScenarioUi.Invoke(window, "Open Startup apps in Windows Settings") && links.Opened.SequenceEqual(["ms-settings:startupapps"]), "the link opens the Startup apps page");

        // The recommended taskbar card (step 4): keep the tray icon visible. Instructions, a button that opens the Taskbar page of Windows Settings, and the looping guide.
        window.TaskbarStepCard.StartBringIntoView();
        await Settle(500);
        Check(window.TaskbarStepCard.Visibility == Visibility.Visible && AutomationProperties.GetName(window.TaskbarStepCard) == "Keep TimeTug in view", "step 4 has the keep-TimeTug-in-view card: " + AutomationProperties.GetName(window.TaskbarStepCard));
        var cardTexts = Texts(window.TaskbarStepCard);
        Check(cardTexts.Contains("Recommended: keep TimeTug in view") && cardTexts.Contains("1.") && cardTexts.Contains("2.")
            && cardTexts.Contains("Select Show TimeTug in the taskbar to open the setting.") && cardTexts.Contains("Under Other system tray icons, turn TimeTug on.")
            && cardTexts.Any(t => t.StartsWith("Or select the arrow next to the clock and drag", StringComparison.Ordinal)), "it is marked recommended and gives the numbered steps and the drag alternative: " + string.Join(" | ", cardTexts));
        Check(window.TaskbarSettingsButton is { Visibility: Visibility.Visible, Content: TextBlock { Text: "Show TimeTug in the taskbar" } } && AutomationProperties.GetName(window.TaskbarSettingsButton) == "Show TimeTug in the taskbar. Opens Windows Settings.", "the button reads Show TimeTug in the taskbar and is named: " + AutomationProperties.GetName(window.TaskbarSettingsButton));
        Check(window.TaskbarErrorText.Visibility == Visibility.Collapsed, "no error is showing");
        var opened = links.Opened.Count;
        Check(Invoke(window, "Show TimeTug in the taskbar. Opens Windows Settings.") && await WaitFor(() => links.Opened.Count == opened + 1, 3000), "the button can be invoked by name and opens a link");
        Check(links.Opened[^1] == "ms-settings:taskbar", "to the Taskbar page of Windows Settings: " + links.Opened[^1]);
        links.Succeeds = false;
        Check(Invoke(window, "Show TimeTug in the taskbar. Opens Windows Settings.") && await WaitFor(() => window.TaskbarErrorText.Visibility == Visibility.Visible, 3000), "a page that does not open shows an error");
        Check(window.TaskbarErrorText.Text == "Couldn’t open Windows Settings. Open Settings, then Personalization, then Taskbar.", "in words: " + window.TaskbarErrorText.Text);
        Check(diagnostics.Entries.Any(e => e.Category == "taskbar" && e.Level == "warning") && diagnostics.Entries.All(e => !e.Message.Contains("ms-settings", StringComparison.Ordinal)), "and logs a warning without content");
        Check(model.Step == WelcomeStep.Handy, "the window carries on");
        links.Succeeds = true;
        Check(Invoke(window, "Show TimeTug in the taskbar. Opens Windows Settings.") && await WaitFor(() => window.TaskbarErrorText.Visibility == Visibility.Collapsed, 3000), "the next press that opens clears the error");

        // The guide: the recording loops (an animated bitmap) while animations are on, and is a still picture of its last frame when Windows has them off.
        Check(window.GuideAsset == "Assets/taskbar-guide.gif" && window.TaskbarGuidePicture.Source is BitmapImage, "the guide is the animated recording (loaded when step 4 appeared)");
        var animated = (BitmapImage)window.TaskbarGuidePicture.Source;
        Check(await WaitFor(() => animated.PixelWidth > 0, 5000), "the recording loads");
        Check(animated.IsAnimatedBitmap && animated.AutoPlay && animated.PixelWidth == 640, $"it is an animated picture that plays on its own, {animated.PixelWidth} px wide");
        Check(AutomationProperties.GetName(window.TaskbarGuidePicture).StartsWith("Guide. In Windows Settings, open Personalization, then Taskbar", StringComparison.Ordinal), "it has a text alternative: " + AutomationProperties.GetName(window.TaskbarGuidePicture));
        await Shot("handy-taskbar");

        // It really moves: the picture's own rectangle on the screen is copied again and again over a little more than one loop (about 8 seconds) and the copies are compared
        // byte for byte. The Windows desktop is needed for this, so a machine without a screen only notes it.
        async Task<List<string>> SampleWindowAsync(int count, int gap)
        {
            var samples = new List<string>();
            for (var i = 0; i < count; i++)
            {
                // The picture's place on the screen: the window's outer position, the frame and title bar (outer size minus client size), then the picture inside the content.
                var content = (FrameworkElement)window.Content;
                var origin = window.TaskbarGuidePicture.TransformToVisual(content).TransformPoint(new Windows.Foundation.Point(0, 0));
                var scale = content.XamlRoot.RasterizationScale;
                var frame = (window.AppWindow.Size.Width - window.AppWindow.ClientSize.Width) / 2;
                var titleBar = window.AppWindow.Size.Height - window.AppWindow.ClientSize.Height - frame;
                var area = new PixelRect(
                    window.AppWindow.Position.X + frame + (int)(origin.X * scale), window.AppWindow.Position.Y + titleBar + (int)(origin.Y * scale),
                    (int)(window.TaskbarGuidePicture.ActualWidth * scale), (int)(window.TaskbarGuidePicture.ActualHeight * scale));
                var file = Path.Combine(run.Folder, "guide-sample.png");
                var capture = await ScreenRegion.CaptureAsync(area, file);
                if (capture.Succeeded)
                {
                    samples.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(file, cancellation))));
                }

                await Settle(gap);
            }

            return samples;
        }

        var playing = await SampleWindowAsync(12, 700);
        var moved = playing.Distinct().Count();
        if (playing.Count >= 8 && moved >= 3)
        {
            Check(true, $"the animated guide changes on screen over time ({moved} different pictures in {playing.Count} copies of the picture)");
        }
        else
        {
            // Only noted, never failed: a hosted runner's desktop may not repaint or copy faster than it is asked to (the ARM64 runner showed one picture for the whole loop
            // while the bitmap itself reported an animation), so the screen alone cannot prove a fault. The bitmap's own properties above are checked.
            run.Info($"the animation was not seen moving on this machine's screen ({moved} different pictures in {playing.Count} of 12 copies)");
        }

        uiSettings.AnimationsEnabled = false;
        uiSettings.Raise();
        Check(await WaitFor(() => window.GuideAsset == "Assets/taskbar-guide-still.png", 3000), "with animations off in Windows the guide switches to a still picture");
        Check(await WaitFor(() => window.TaskbarGuidePicture.Source is BitmapImage { PixelWidth: > 0 }, 5000) && !((BitmapImage)window.TaskbarGuidePicture.Source).IsAnimatedBitmap, "and it does not move");
        Check(AutomationProperties.GetName(window.TaskbarGuidePicture).Length > 0, "and keeps its text alternative");
        await Shot("handy-taskbar-still");
        // The first copy may still show the swap being painted; what follows should not change at all (noted, not failed, when a slow machine is still repainting).
        var still = (await SampleWindowAsync(6, 700)).Skip(1).ToList();
        if (still.Count >= 4 && still.Distinct().Count() == 1)
        {
            Check(true, $"and the still stays still on screen (1 picture in the last {still.Count} copies)");
        }
        else
        {
            run.Info($"the still picture was not seen staying still ({still.Distinct().Count()} different pictures in the last {still.Count} copies)");
        }

        uiSettings.AnimationsEnabled = true;
        uiSettings.Raise();
        Check(await WaitFor(() => window.GuideAsset == "Assets/taskbar-guide.gif", 3000), "with animations on again it plays again");

        // A narrow window: the card and the picture shrink to fit, nothing runs off the side.
        var wideSize = window.AppWindow.Size;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32(440, wideSize.Height));
        await Settle(800);
        window.TaskbarStepCard.StartBringIntoView();
        await Settle(400);
        var content = (FrameworkElement)window.Content;
        Check(window.TaskbarStepCard.ActualWidth > 0 && window.TaskbarStepCard.ActualWidth <= content.ActualWidth && window.TaskbarGuidePicture.ActualWidth <= window.TaskbarStepCard.ActualWidth,
            $"in a narrow window the card ({window.TaskbarStepCard.ActualWidth:0} px) fits the window ({content.ActualWidth:0} px) and holds the picture ({window.TaskbarGuidePicture.ActualWidth:0} px)");
        var label = (TextBlock)window.TaskbarSettingsButton.Content;
        Check(window.TaskbarSettingsButton.ActualWidth <= window.TaskbarStepCard.ActualWidth && label.ActualWidth <= window.TaskbarSettingsButton.ActualWidth && label.ActualHeight > 0,
            $"the button ({window.TaskbarSettingsButton.ActualWidth:0} px) fits the card and its label wraps instead of being cut off ({label.ActualWidth:0} x {label.ActualHeight:0} px)");
        await Shot("handy-taskbar-narrow");
        window.AppWindow.Resize(wideSize);
        await Settle(400);

        // The window is left open: closing the only window would end the app before the result is written.
        return run.Finish();
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, int milliseconds, CancellationToken cancellation)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < milliseconds)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50, cancellation);
        }

        return condition();
    }

    // The text of every visible text block under a node, in order.
    private static List<string> Texts(DependencyObject node)
    {
        var found = new List<string>();
        void Collect(DependencyObject n)
        {
            if (n is UIElement { Visibility: Visibility.Collapsed })
            {
                return;
            }

            if (n is TextBlock { Text.Length: > 0 } t)
            {
                found.Add(t.Text);
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(n); i++)
            {
                Collect(VisualTreeHelper.GetChild(n, i));
            }
        }

        Collect(node);
        return found;
    }

    // Every visible button, text box and progress ring (and those in open dialogs) must have a name Narrator can read.
    private static List<string> Unnamed(Window window)
    {
        var found = new List<string>();
        var root = (FrameworkElement)window.Content;
        Walk(root, found);
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
        {
            Walk(popup.Child, found);
        }

        return found;
    }

    private static void Walk(DependencyObject node, List<string> unnamed)
    {
        if (node is UIElement { Visibility: Visibility.Collapsed })
        {
            return;
        }

        if (node is Button or HyperlinkButton or TextBox or PasswordBox or ProgressRing)
        {
            var name = AutomationProperties.GetName(node);
            var hasText = node is ContentControl { Content: string { Length: > 0 } };
            if (string.IsNullOrWhiteSpace(name) && !hasText)
            {
                unnamed.Add(node.GetType().Name + (node is FrameworkElement { Name.Length: > 0 } f ? " " + f.Name : ""));
            }
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            Walk(VisualTreeHelper.GetChild(node, i), unnamed);
        }
    }

    private static bool Invoke(Window window, string automationName)
    {
        var button = Find((FrameworkElement)window.Content, automationName);
        if (button is null || !button.IsEnabled)
        {
            return false;
        }

        var peer = FrameworkElementAutomationPeer.CreatePeerForElement(button);
        if (peer?.GetPattern(PatternInterface.Invoke) is not IInvokeProvider invoke)
        {
            return false;
        }

        invoke.Invoke();
        return true;
    }

    private static Control? Find(DependencyObject node, string automationName)
    {
        if (node is Button or HyperlinkButton && AutomationProperties.GetName(node) == automationName)
        {
            return (Control)node;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (Find(VisualTreeHelper.GetChild(node, i), automationName) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
#endif
