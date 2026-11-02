#if SELFTEST
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;
using TimeTug.Presentation.Welcome;

namespace TimeTug.App.SelfTest.Scenarios;

/// <summary>
/// Scenario <c>welcome</c>: opens the welcome window against a scripted engine and walks it through the Connect step, a
/// browser sign-in and its cancel, the iCloud form, a failed sign-in and the connected step, saving a screenshot at each stop.
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
        using var engine = new ScriptedEngineClient(forms) { ShowFormFor = "icloud" };
        engine.Push(Scenes.Empty());
        var startup = new ScriptedStartupTask();
        var links = new RecordingSystemLauncher();
        var launch = new LaunchAtLoginViewModel(startup, links, new DispatcherQueueDispatcher(queue), text);
        var window = WelcomeWindowFactory.Create(engine, forms, text, new FixedClock(Noon), launchAtLogin: launch);
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
        engine.Push(Scenes.Connected(Noon));
        Check(await WaitFor(() => model.Accounts.ListAccounts.Count == 1, 4000), "the new account is in the list");
        await Settle(600);
        Check(model.Step == WelcomeStep.Connect, "an account added with + does not move the window on");
        Check(AccountsListUi.ExposesListPatterns(box), "the list exposes the Selection pattern and its row the SelectionItem pattern" + ": " + AccountsListUi.ListPatternReport(box).Detail);
        Check(AccountsListUi.RowNames(box).SequenceEqual(["Google, you@example.com, Connected"]), "the row reads as provider, name and status: " + string.Join(" | ", AccountsListUi.RowNames(box)));
        Check(box.EmptyLineText.Visibility == Visibility.Collapsed && window.Continue.IsEnabled, "the empty line is gone and Continue is enabled");
        Check(Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("connect-with-account");

        // Step 3: connected.
        Check(Invoke(window, "Continue"), "Continue can be invoked by name");
        await Settle(1000);
        Check(model.Step == WelcomeStep.Connected, "Continue moves to the Connected step");
        Check(model.Accounts.Accounts.Count == 1 && model.Accounts.Accounts[0].Summary.StartsWith("Google · you@example.com · 3 calendars", StringComparison.Ordinal), "the account is summarised");
        Check(Unnamed(window).Count == 0, "every control on the Connected step has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("connected");

        // Launch at login: unchecked until the person ticks it, and shown as Windows says after the tick.
        await Settle(400);
        Check(model.ShowLaunchAtLogin && ((FrameworkElement)window.LaunchCheckBox.Parent).Visibility == Visibility.Visible, "the second step offers the launch-at-login checkbox");
        Check(window.LaunchCheckBox.IsChecked == false && window.LaunchCheckBox.IsEnabled, "it starts unchecked");
        Check(AutomationProperties.GetName(window.LaunchCheckBox) == "Start TimeTug when I sign in" && window.LaunchCheckBox.Content is "Start TimeTug when I sign in", "and is named: " + AutomationProperties.GetName(window.LaunchCheckBox));
        Check(startup.Calls.SequenceEqual(["get"]) && startup.State == StartupState.Disabled, "Windows was only asked for the state; nothing was enabled");
        Check(ScenarioUi.Toggle(window.LaunchCheckBox), "the checkbox can be ticked through UI Automation");
        await Settle(500);
        Check(startup.Calls.SequenceEqual(["get", "enable"]) && window.LaunchCheckBox.IsChecked == true, "a tick requests enabling and the box stays ticked because Windows enabled it");
        await Shot("connected-launch-ticked");
        Check(ScenarioUi.Toggle(window.LaunchCheckBox), "unticking");
        await Settle(500);
        Check(startup.Calls.Last() == "disable" && window.LaunchCheckBox.IsChecked == false, "turns it off again");
        // The person has switched the task off in Windows: the tick is refused and the box says so, with the link.
        startup.StateAfterRequest = StartupState.DisabledByUser;
        Check(ScenarioUi.Toggle(window.LaunchCheckBox), "ticking while Windows is set to refuse");
        await Settle(500);
        Check(window.LaunchCheckBox.IsChecked == false && !window.LaunchCheckBox.IsEnabled, "the box goes back to unchecked and is disabled: Windows decides");
        Check(window.LaunchSettingsLink.Visibility == Visibility.Visible && window.LaunchNoteText.Text.Contains("Startup apps", StringComparison.Ordinal), "with the reason and the link: " + window.LaunchNoteText.Text);
        await Shot("connected-launch-blocked");
        Check(ScenarioUi.Invoke(window, "Open Startup apps in Windows Settings") && links.Opened.SequenceEqual(["ms-settings:startupapps"]), "the link opens the Startup apps page");
        Check(Invoke(window, "Show a test tug screen") && engine.Sent.Contains("testTug"), "Test tug sends the test takeover");

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
