using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Tug;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace TimeTug.App.Tug;

/// <summary>The parts of building a tug window that the primary and the mirror windows share.</summary>
internal static class TugWindowSetup
{
    /// <summary>
    /// Makes the window borderless, topmost and exactly as large as the monitor, taskbar area included, without a taskbar
    /// button of its own. A window with <paramref name="activate"/> false is shown without taking the keyboard.
    /// </summary>
    public static void PlaceOnMonitor(Window window, TugMonitorPlan plan, bool activate)
    {
        var monitor = plan.Monitor;
        var appWindow = window.AppWindow;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        appWindow.IsShownInSwitchers = false;
        var handle = WindowNative.GetWindowHandle(window);
        TugWindowChrome.StyleAsFullMonitorOverlay(handle);
        if (!activate)
        {
            TugWindowChrome.MakeNonActivating(handle);
        }

        appWindow.MoveAndResize(new RectInt32(monitor.X, monitor.Y, monitor.Width, monitor.Height));
        if (activate)
        {
            window.Activate();
        }
        else
        {
            appWindow.Show(false);
        }

        // Last word on position and z-order: above every other topmost window (the taskbar is one) and exactly on the monitor,
        // even if the move crossed a DPI boundary and Windows resized the window on the way.
        TugWindowChrome.PlaceTopmost(handle, monitor.X, monitor.Y, monitor.Width, monitor.Height, activate);
    }

    /// <summary>Acrylic with a navy tint, or a solid fill when transparency is off, acrylic is unavailable or a contrast theme is on.</summary>
    public static void ApplyBackdrop(Window window, Panel root, TugViewModel model, TugTheme theme)
    {
        if (model.Backdrop == TugBackdropKind.Acrylic && TugBackdrop.IsSupported)
        {
            window.SystemBackdrop = new TugBackdrop();
            // A fully transparent brush still takes hits, so a click on any part of the screen reaches the window.
            root.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            root.Background = theme.Window;
        }

        root.RequestedTheme = theme.WindowIsDark ? ElementTheme.Dark : ElementTheme.Light;
    }

    /// <summary>A slow breathing of the urgent countdown: the Mac's pulse. Start and stop it with the view model's flag.</summary>
    public static Storyboard CreatePulse(UIElement target)
    {
        var scale = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
        target.RenderTransform = scale;
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        var storyboard = new Storyboard();
        foreach (var property in new[] { "ScaleX", "ScaleY" })
        {
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 1.03,
                Duration = new Duration(TimeSpan.FromSeconds(1)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(animation, scale);
            Storyboard.SetTargetProperty(animation, property);
            storyboard.Children.Add(animation);
        }

        return storyboard;
    }

    public static void StopPulse(Storyboard? pulse, UIElement target)
    {
        pulse?.Stop();
        if (target.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 1;
            scale.ScaleY = 1;
        }
    }

    /// <summary>
    /// Tells Narrator (and any other UI Automation client) something happened, without moving the keyboard focus: a UI
    /// Automation notification event raised from <paramref name="element"/>.
    /// </summary>
    public static void Announce(UIElement element, string text, string activityId)
    {
        var peer = FrameworkElementAutomationPeer.FromElement(element) ?? FrameworkElementAutomationPeer.CreatePeerForElement(element);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.ImportantMostRecent, text, activityId);
    }

    public static void Name(DependencyObject element, string name) => AutomationProperties.SetName(element, name);
}
