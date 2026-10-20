#if SELFTEST
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.Presentation.Engine;
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
        var window = WelcomeWindowFactory.Create(engine, forms, text, new FixedClock(Noon));
        var model = window.ViewModel;
        window.Activate();
        await SelfTestRunner.WaitForFrameAsync(window, cancellation);

        Task Shot(string label) => run.ShotAsync(window, label);

        async Task Settle(int milliseconds = 800) => await Task.Delay(milliseconds, cancellation);

        // Step 1: the list of places to connect.
        Check(model.Step == WelcomeStep.Connect, "starts on the Connect step");
        Check(model.Providers.Select(p => p.Id).SequenceEqual(["google", "microsoft", "icloud", "caldav"]), "providers are listed in the engine's order");
        Check(!model.Providers[1].IsEnabled && model.Providers[1].Hint.Contains("isn’t set up", StringComparison.Ordinal), "an unconfigured kind is disabled with the reason");
        Check(Unnamed(window).Count == 0, "every control has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("connect");

        // A browser sign-in shows its progress and can be cancelled.
        Check(Invoke(window, "Connect Google"), "the Google button can be invoked by name");
        await Settle();
        Check(engine.Sent.Contains("add:google"), "Google sends account.add");
        Check(model.Accounts.IsBusy && model.Providers.All(p => !p.IsEnabled), "while signing in every provider is disabled");
        await Shot("waiting");
        Check(Invoke(window, "Cancel sign-in"), "the Cancel button can be invoked by name");
        await Settle(400);
        Check(engine.Sent.Contains("cancel") && !model.Accounts.IsBusy, "Cancel sends account.cancel and ends the wait");

        // iCloud asks for its details in a dialog built from the engine's field descriptions.
        await model.ConnectAsync("icloud");
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

        // A failed sign-in stays on this step with the error beside that provider.
        await model.ConnectAsync("caldav");
        engine.Push(Scenes.Empty(new AccountActivity(false, "Sign-in failed: the server rejected that user name or password.", "inApp")));
        await Settle();
        Check(model.Step == WelcomeStep.Connect && model.Providers[3].HasError && !model.Providers[2].HasError, "a failure shows beside that provider and keeps the step");
        await Shot("error");

        // Step 2: connected.
        Check(Invoke(window, "Connect Google"), "Google can be tried again");
        engine.Push(Scenes.Connected(Noon));
        await Settle(1000);
        Check(model.Step == WelcomeStep.Connected, "a new account moves to the Connected step");
        Check(model.Accounts.Accounts.Count == 1 && model.Accounts.Accounts[0].Summary.StartsWith("Google · you@example.com · 3 calendars", StringComparison.Ordinal), "the account is summarised");
        Check(Unnamed(window).Count == 0, "every control on the Connected step has an automation name: " + string.Join(", ", Unnamed(window)));
        await Shot("connected");
        Check(Invoke(window, "Show a test tug screen") && engine.Sent.Contains("testTug"), "Test tug sends the test takeover");

        // The window is left open: closing the only window would end the app before the result is written.
        return run.Finish();
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
