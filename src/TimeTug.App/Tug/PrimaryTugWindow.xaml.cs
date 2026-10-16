using System.ComponentModel;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Tug;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using WinRT.Interop;

namespace TimeTug.App.Tug;

/// <summary>
/// The tug on the main display: hero, live countdown, title, details, Join, Snooze, Dismiss and the hints. Everything it
/// shows comes from <see cref="TugViewModel"/>; this file only draws it, routes keys and clicks, and reports whether the
/// window really has the keyboard. Join, Snooze and Dismiss are plain buttons, so they work by mouse alone even when the
/// window never got the keyboard.
/// </summary>
internal sealed partial class PrimaryTugWindow : Window
{
    private static readonly TimeSpan FocusGrace = TimeSpan.FromMilliseconds(600);

    private readonly bool _announce;
    private readonly TugTheme _theme;
    private Storyboard? _pulse;
    private bool _lastReportedFocus = true;
    private bool _everActive;
    private bool _closed;

    public PrimaryTugWindow(TugViewModel model, TugMonitorPlan plan, bool announce)
    {
        ViewModel = model;
        Plan = plan;
        _announce = announce;
        InitializeComponent();
        _theme = TugTheme.For(model.Palette);
        Title = model.WindowTitle;
        TugWindowSetup.ApplyBackdrop(this, Root, model, _theme);
        BuildStaticContent();
        Render();
        model.PropertyChanged += OnModelChanged;
        Root.PreviewKeyDown += OnPreviewKeyDown;
        Root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(OnPointerPressed), handledEventsToo: true);
        Activated += OnActivated;
        Closed += OnClosed;
        Root.Loaded += OnLoaded;
        TugWindowSetup.PlaceOnMonitor(this, plan, activate: true);
    }

    public TugViewModel ViewModel { get; }

    public TugMonitorPlan Plan { get; }

    /// <summary>The announcements raised so far (kept for the self-test, which checks that one was made).</summary>
    public List<string> Announcements { get; } = [];

    private nint Handle => WindowNative.GetWindowHandle(this);

    // ---- building and drawing -----------------------------------------------------------------------------------

    private void BuildStaticContent()
    {
        var model = ViewModel;
        TugWindowSetup.Name(Hero, model.HeroAutomationName);
        AutomationProperties.SetAccessibilityView(Hero, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(CountdownLabel, AccessibilityView.Raw);
        Hero.Visibility = _theme.IsContrast ? Visibility.Collapsed : Visibility.Visible;
        HeroFrame.Visibility = Hero.Visibility;

        TitleText.Text = model.Title;
        TugWindowSetup.Name(TitleText, model.Title);
        DetailsText.Text = model.Details;
        HintsText.Text = model.Hints;
        TugWindowSetup.Name(HintsText, model.SpokenHints);

        JoinLabel.Text = model.JoinText;
        TugWindowSetup.Name(JoinButton, model.JoinAutomationName);
        JoinButton.Visibility = model.HasJoin ? Visibility.Visible : Visibility.Collapsed;

        SnoozeLabel.Text = model.SnoozeText;
        TugWindowSetup.Name(SnoozeButton, model.SnoozeAutomationName);
        SnoozeButton.Visibility = model.HasSnooze ? Visibility.Visible : Visibility.Collapsed;
        foreach (var choice in model.SnoozeChoices)
        {
            var item = new MenuFlyoutItem { Text = choice.Title };
            TugWindowSetup.Name(item, choice.Title);
            var duration = choice.Duration;
            item.Click += (_, _) => Run(ViewModel.SnoozeAsync(duration));
            SnoozeMenu.Items.Add(item);
        }

        DismissButton.Content = model.DismissText;
        TugWindowSetup.Name(DismissButton, model.DismissAutomationName);

        ClickBannerText.Text = model.ClickToRespondText;
        TugWindowSetup.Name(ClickBanner, model.ClickToRespondText);

        JoinFailedText.Text = model.JoinFailedText;
        TugWindowSetup.Name(JoinLinkBox, model.JoinLinkAutomationName);
        CopyButton.Content = model.CopyLinkText;
        TugWindowSetup.Name(CopyButton, model.CopyLinkAutomationName);
        CopiedText.Text = model.LinkCopiedText;

        ApplyColours();
    }

    private void ApplyColours()
    {
        var theme = _theme;
        TitleText.Foreground = theme.Text;
        DetailsText.Foreground = theme.Muted;
        HintsText.Foreground = theme.Hint;
        CountdownLabel.Foreground = theme.Muted;
        JoinFailedText.Foreground = theme.Text;
        CopiedText.Foreground = theme.Muted;
        ClickBannerText.Foreground = theme.IsContrast ? theme.OnAccent : theme.Text;
        ClickBanner.Background = theme.IsContrast ? theme.Accent : TugTheme.Solid(TugBrand.Blue, 0xCC);
        if (theme.IsContrast)
        {
            // A contrast theme draws buttons in its own colours: window and text, and highlight when pointed at. Join stays filled.
            StyleButton(JoinButton, theme.Accent, theme.OnAccent, hover: theme.Window, pressed: theme.Window, border: theme.Text, foregroundHover: theme.Text);
            foreach (var button in new[] { SnoozeButton, DismissButton, CopyButton })
            {
                StyleButton(button, theme.Window, theme.Text, hover: theme.Accent, pressed: theme.Accent, border: theme.Text, foregroundHover: theme.OnAccent);
            }

            return;
        }

        StyleButton(JoinButton, theme.Accent, theme.OnAccent, hoverOpacity: 0.92, pressedOpacity: 0.75);
        StyleButton(SnoozeButton, TugTheme.Solid(new TugColor(255, 255, 255), 26), theme.Text, hover: TugTheme.Solid(new TugColor(255, 255, 255), 46), pressed: TugTheme.Solid(new TugColor(255, 255, 255), 64), border: TugTheme.Solid(new TugColor(255, 255, 255), 71));
        StyleButton(DismissButton, TugTheme.Solid(new TugColor(0, 0, 0), 0), theme.Muted, hover: TugTheme.Solid(new TugColor(0, 0, 0), 0), pressed: TugTheme.Solid(new TugColor(0, 0, 0), 0), foregroundHover: TugTheme.Solid(new TugColor(255, 255, 255), 230));
    }

    private static void StyleButton(
        Button button,
        Microsoft.UI.Xaml.Media.Brush normal,
        Microsoft.UI.Xaml.Media.Brush foreground,
        double hoverOpacity = 1,
        double pressedOpacity = 1,
        Microsoft.UI.Xaml.Media.Brush? hover = null,
        Microsoft.UI.Xaml.Media.Brush? pressed = null,
        Microsoft.UI.Xaml.Media.Brush? border = null,
        Microsoft.UI.Xaml.Media.Brush? foregroundHover = null)
    {
        var none = TugTheme.Solid(new TugColor(0, 0, 0), 0);
        var hoverBrush = hover ?? Dimmed(normal, hoverOpacity);
        var pressedBrush = pressed ?? Dimmed(normal, pressedOpacity);
        var outline = border ?? none;
        var hoverForeground = foregroundHover ?? foreground;
        button.Resources["ButtonBackground"] = normal;
        button.Resources["ButtonBackgroundPointerOver"] = hoverBrush;
        button.Resources["ButtonBackgroundPressed"] = pressedBrush;
        button.Resources["ButtonBackgroundDisabled"] = normal;
        button.Resources["ButtonForeground"] = foreground;
        button.Resources["ButtonForegroundPointerOver"] = hoverForeground;
        button.Resources["ButtonForegroundPressed"] = hoverForeground;
        button.Resources["ButtonBorderBrush"] = outline;
        button.Resources["ButtonBorderBrushPointerOver"] = outline;
        button.Resources["ButtonBorderBrushPressed"] = outline;
    }

    private static Microsoft.UI.Xaml.Media.Brush Dimmed(Microsoft.UI.Xaml.Media.Brush brush, double opacity) =>
        brush is Microsoft.UI.Xaml.Media.SolidColorBrush solid
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(solid.Color) { Opacity = opacity }
            : brush;

    /// <summary>Draws the parts of the model that change while the tug is up: the countdown and the plan's flags.</summary>
    private void Render()
    {
        var model = ViewModel;
        CountdownLabel.Text = model.CountdownLabel ?? string.Empty;
        CountdownLabel.Visibility = model.HasCountdownLabel ? Visibility.Visible : Visibility.Collapsed;
        CountdownValue.Text = model.CountdownValue;
        CountdownValue.FontSize = model.IsCountdown ? 88 : 48;
        CountdownValue.Foreground = _theme.HeadlineBrushes[model.Tone];
        TugWindowSetup.Name(CountdownValue, model.SpokenCountdown);

        ClickBanner.Visibility = model.ShowClickToRespond ? Visibility.Visible : Visibility.Collapsed;
        JoinFailedPanel.Visibility = model.HasJoinFailed ? Visibility.Visible : Visibility.Collapsed;
        if (model.HasJoinFailed && JoinLinkBox.Text != model.JoinFailedUrl)
        {
            JoinLinkBox.Text = model.JoinFailedUrl ?? string.Empty;
        }

        UpdatePulse();
    }

    private void UpdatePulse()
    {
        if (ViewModel.PulseEnabled && !_closed)
        {
            _pulse ??= TugWindowSetup.CreatePulse(CountdownValue);
            _pulse.Begin();
        }
        else
        {
            TugWindowSetup.StopPulse(_pulse, CountdownValue);
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_closed)
        {
            Render();
        }
    }

    // ---- focus, keys and clicks ---------------------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Re-assert the position once the window is laid out (a DPI change on the way may have resized it).
        TugWindowChrome.PlaceTopmost(Handle, Plan.Monitor.X, Plan.Monitor.Y, Plan.Monitor.Width, Plan.Monitor.Height, activate: true);
        FocusDefaultButton();
        if (_announce)
        {
            Announce();
        }

        _ = CheckKeyboardAfterGraceAsync();
    }

    private void Announce()
    {
        Announcements.Add(ViewModel.Announcement);
        TugWindowSetup.Announce(TitleText, ViewModel.Announcement, "TimeTugTug");
    }

    /// <summary>Join when there is a link, otherwise Dismiss: what Return does.</summary>
    private void FocusDefaultButton()
    {
        var target = ViewModel.HasJoin ? JoinButton : DismissButton;
        target.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Windows may refuse to hand the keyboard to a window that appears while the person works elsewhere. If none of the
    /// activation events says this window is active shortly after it appeared, the plan is told so, and it shows
    /// "Click anywhere to respond".
    /// </summary>
    private async Task CheckKeyboardAfterGraceAsync()
    {
        await Task.Delay(FocusGrace);
        if (_closed)
        {
            return;
        }

        var hasKeyboard = _everActive || TugWindowChrome.IsForeground(Handle);
        ActivationLog.Add($"grace: everActive={_everActive} fg={TugWindowChrome.IsForeground(Handle)} -> {(hasKeyboard ? "keyboard" : "no keyboard")}");
        if (!hasKeyboard)
        {
            await ReportFocusAsync(false);
        }
    }

    /// <summary>Activation changes seen so far, newest last (for diagnosing focus problems).</summary>
    public List<string> ActivationLog { get; } = [];

    /// <summary>
    /// Called by the host just before it closes or replaces this window: the deactivation that closing causes must not be
    /// reported as "the keyboard was lost", or the replacement window would show the click-anywhere banner for nothing.
    /// </summary>
    public void Retire() => _closed = true;

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (_closed)
        {
            return;
        }

        ActivationLog.Add($"{args.WindowActivationState} fg={TugWindowChrome.IsForeground(Handle)}");
        var active = args.WindowActivationState != WindowActivationState.Deactivated;
        if (active)
        {
            _everActive = true;
        }

        if (active != _lastReportedFocus && (active || _everActive))
        {
            _ = ReportFocusAsync(active);
        }
    }

    private async Task ReportFocusAsync(bool hasKeyboard)
    {
        _lastReportedFocus = hasKeyboard;
        await ViewModel.ReportFocusAsync(hasKeyboard);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // The first click on any part of the screen takes the keyboard (a click always reaches a topmost window).
        if (!_lastReportedFocus || !_everActive)
        {
            TakeFocus(keepFocusedButton: true);
        }
    }

    /// <summary>When the window turns out to be the foreground window after asking for it, the plan hears that it has the keyboard.</summary>
    private void ReportIfForeground()
    {
        if ((!_lastReportedFocus || ViewModel.ShowClickToRespond) && TugWindowChrome.IsForeground(Handle))
        {
            _everActive = true;
            _ = ReportFocusAsync(true);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var key = e.Key switch
        {
            VirtualKey.Enter => (TugKey?)TugKey.Return,
            VirtualKey.Escape => TugKey.Escape,
            VirtualKey.Number1 or VirtualKey.NumberPad1 => TugKey.Digit1,
            VirtualKey.Number5 or VirtualKey.NumberPad5 => TugKey.Digit5,
            VirtualKey.Number0 or VirtualKey.NumberPad0 => TugKey.Digit0,
            _ => null,
        };

        // A button that has the focus answers Return itself (Snooze opens its menu), so nobody ends up joining a meeting by
        // pressing Return on the Snooze button.
        if (key is not { } tugKey || (tugKey == TugKey.Return && FocusManager.GetFocusedElement(Root.XamlRoot) is Button))
        {
            return;
        }

        if (ViewModel.Resolve(tugKey) is not null)
        {
            e.Handled = true;
            Run(ViewModel.HandleKeyAsync(tugKey));
        }
    }

    /// <summary>Starts a response; the controller catches and records its own failures, so nothing is left to await here.</summary>
    private static void Run(ValueTask work) => _ = work.AsTask();

    private static void Run(ValueTask<bool> work) => _ = work.AsTask();

    private void OnJoinClick(object sender, RoutedEventArgs e) => Run(ViewModel.JoinAsync());

    private void OnDismissClick(object sender, RoutedEventArgs e) => Run(ViewModel.DismissAsync());

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (ViewModel.JoinFailedUrl is not { Length: > 0 } url)
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(url);
        Clipboard.SetContent(package);
        CopiedText.Visibility = Visibility.Visible;
        TugWindowSetup.Announce(CopiedText, ViewModel.LinkCopiedText, "TimeTugCopied");
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        ViewModel.PropertyChanged -= OnModelChanged;
        TugWindowSetup.StopPulse(_pulse, CountdownValue);
    }

    /// <summary>Brings this window forward and takes the keyboard (a click on a mirror window calls it).</summary>
    public void TakeFocus(bool keepFocusedButton = false)
    {
        Activate();
        if (!keepFocusedButton || FocusManager.GetFocusedElement(Root.XamlRoot) is not Button)
        {
            FocusDefaultButton();
        }

        ReportIfForeground();
    }
}
