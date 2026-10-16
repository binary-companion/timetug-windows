#if SELFTEST
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TimeTug.App.SelfTest;

/// <summary>Helpers for scenarios that drive a window the way Narrator or a keyboard user would: through UI Automation.</summary>
internal static class ScenarioUi
{
    /// <summary>Every visible button, text box and progress ring without an automation name (Narrator could not read it).</summary>
    public static List<string> Unnamed(Window window)
    {
        var found = new List<string>();
        Walk((FrameworkElement)window.Content, found);
        return found;
    }

    /// <summary>Invokes the button with this automation name through its UI Automation peer. False when there is none or it is disabled.</summary>
    public static bool Invoke(Window window, string automationName)
    {
        var button = FindButton((FrameworkElement)window.Content, automationName);
        if (button is null || !button.IsEnabled || button.Visibility != Visibility.Visible)
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

    public static bool HasButton(Window window, string automationName) =>
        FindButton((FrameworkElement)window.Content, automationName) is { Visibility: Visibility.Visible };

    /// <summary>The first element of this type and x:Name (or null).</summary>
    public static T? FindNamed<T>(Window window, string name)
        where T : FrameworkElement => Find<T>((FrameworkElement)window.Content, name);

    private static T? Find<T>(DependencyObject node, string name)
        where T : FrameworkElement
    {
        if (node is T match && match.Name == name)
        {
            return match;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (Find<T>(VisualTreeHelper.GetChild(node, i), name) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private static Button? FindButton(DependencyObject node, string automationName)
    {
        if (node is Button button && AutomationProperties.GetName(node) == automationName)
        {
            return button;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (FindButton(VisualTreeHelper.GetChild(node, i), automationName) is { } hit)
            {
                return hit;
            }
        }

        return null;
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
}
#endif
