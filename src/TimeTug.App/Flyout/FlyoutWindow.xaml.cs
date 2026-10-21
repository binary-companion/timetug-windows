using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using TimeTug.Platform.Flyout;
using TimeTug.Platform.Tug;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace TimeTug.App.Flyout;

/// <summary>
/// The tray flyout: a frameless, rounded window next to the tray icon with the problems, the header (icon, weekday, summary, a disabled
/// Settings button), a scrolling list of cards under a fixed header, the empty state, the "No more meetings" banner and the footer. It
/// closes when it loses the active state and on Esc (it reports both; <see cref="FlyoutHost"/> decides). All content comes from
/// <see cref="FlyoutViewModel"/>, which raises <c>Changed</c> when the clock or the engine changes it; the cards are then rebuilt.
/// It is a plain window class; <see cref="FlyoutSurface"/> adapts it to the host's interface.
/// </summary>
internal sealed partial class FlyoutWindow : Window
{
    private static readonly TimeSpan OpenDuration = TimeSpan.FromMilliseconds(167);

    private readonly FlyoutViewModel _model;
    private readonly IUiDispatcher _ui;
    private readonly ILocalizer _text;
    private readonly IUiSettings _settings;
    private FlyoutPalette _palette;
    private bool _everActive;
    private bool _closing;
    private Storyboard? _opening;

    public FlyoutWindow(FlyoutViewModel model, IUiDispatcher ui, ILocalizer text, IUiSettings settings)
    {
        _model = model;
        _ui = ui;
        _text = text;
        _settings = settings;
        InitializeComponent();
        Title = text.GetString("Flyout_WindowTitle");
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;
        // Each window loads its own copy of the app picture (a brush shared between windows came out empty in the second one).
        foreach (var icon in new[] { HeaderIcon, NoMoreIcon, EmptyIcon })
        {
            icon.Background = new ImageBrush { ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/tug-icon.png")) { DecodePixelWidth = 144 }, Stretch = Stretch.UniformToFill };
        }

        _palette = FlyoutPalette.For(IsDark, settings.HighContrast);
        Rebuild();
        model.Changed += OnModelChanged;
        Root.PreviewKeyDown += OnPreviewKeyDown;
        Activated += OnActivated;
        Closed += OnClosed;
        Root.Loaded += OnLoaded;
    }

    /// <summary>The window lost the active state after having had it.</summary>
    public event Action? WindowDeactivated;

    public event Action? EscapeRequested;

    /// <summary>The cards were rebuilt: the wanted height may have changed.</summary>
    public event Action? ContentRebuilt;

    /// <summary>The window closed, whoever closed it.</summary>
    public event Action? WindowClosed;

    /// <summary>The gear in the header was pressed.</summary>
    public event Action? SettingsPressed;

    /// <summary>True once the open animation was started (never when Windows has animations off).</summary>
    public bool Animated { get; private set; }

    /// <summary>The backdrop in use now.</summary>
    public FlyoutBackdrop Backdrop { get; private set; } = FlyoutBackdrop.Solid;

    /// <summary>True when the window was the foreground window at some point (Windows may refuse to make it so).</summary>
    public bool EverActive => _everActive;

    public nint Handle => WindowNative.GetWindowHandle(this);

    public FlyoutViewModel ViewModel => _model;

    private static bool IsDark => Application.Current.RequestedTheme == ApplicationTheme.Dark;

    // ---- showing and measuring ----------------------------------------------------------------------------------

    /// <summary>The size the content wants at 360 DIP wide, with the list limited so the whole window is at most 520 DIP tall.</summary>
    public SizeInDips MeasureContent()
    {
        var width = FlyoutPlacement.WidthDips;
        ListScroll.MaxHeight = double.PositiveInfinity;
        Page.Measure(new Size(width, double.PositiveInfinity));
        var whole = Page.DesiredSize.Height;
        if (whole > FlyoutPlacement.MaxHeightDips)
        {
            // Everything but the list stays; the list takes what is left and scrolls.
            var chrome = whole - ListScroll.DesiredSize.Height;
            ListScroll.MaxHeight = Math.Max(120, FlyoutPlacement.MaxHeightDips - chrome);
            Page.Measure(new Size(width, double.PositiveInfinity));
            whole = Page.DesiredSize.Height;
        }

        return new SizeInDips(width, Math.Min(whole, FlyoutPlacement.MaxHeightDips));
    }

    public void Present(PixelRect bounds, FlyoutLook look)
    {
        ApplyLook(look);
        var handle = Handle;
        FlyoutWindowChrome.StyleAsFlyout(handle);
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        if (look.Animate)
        {
            Page.Opacity = 0;
        }

        Activate();
        FlyoutWindowChrome.Place(handle, bounds, activate: true);
        if (look.Animate)
        {
            StartOpenAnimation();
        }
        else
        {
            Page.Opacity = 1;
        }
    }

    public void Reposition(PixelRect bounds, FlyoutLook look)
    {
        ActivationLog.Add($"reposition fg={TugWindowChrome.IsForeground(Handle)}");
        ApplyLook(look);
        FlyoutWindowChrome.Place(Handle, bounds, activate: false);
    }

    public void CloseFlyout()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _model.Changed -= OnModelChanged;
        try
        {
            Close();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Already closed by the system.
        }
    }

    private void ApplyLook(FlyoutLook look)
    {
        var acrylic = look.Backdrop == FlyoutBackdrop.Material && DesktopAcrylicController.IsSupported();
        if (acrylic)
        {
            if (SystemBackdrop is not DesktopAcrylicBackdrop)
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }

            // A fully transparent brush still takes hits, so a click anywhere in the window reaches it.
            Root.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            SystemBackdrop = null;
            Root.Background = look.HighContrast ? FlyoutPalette.For(IsDark, true).CardFill : SolidFill();
        }

        Backdrop = acrylic ? FlyoutBackdrop.Material : FlyoutBackdrop.Solid;
        if (look.HighContrast != _palette.IsContrast)
        {
            _palette = FlyoutPalette.For(IsDark, look.HighContrast);
            Rebuild();
        }
    }

    private static Brush SolidFill() =>
        Application.Current.Resources.TryGetValue("SolidBackgroundFillColorBaseBrush", out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(IsDark ? Color(32, 32, 32) : Color(243, 243, 243));

    private static Windows.UI.Color Color(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(255, r, g, b);

    private void StartOpenAnimation()
    {
        var slide = new TranslateTransform { Y = 14 };
        Page.RenderTransform = slide;
        var movement = new DoubleAnimation { From = 14, To = 0, Duration = new Duration(OpenDuration), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(movement, slide);
        Storyboard.SetTargetProperty(movement, "Y");
        var fade = new DoubleAnimation { From = 0, To = 1, Duration = new Duration(OpenDuration), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Storyboard.SetTarget(fade, Page);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _opening = new Storyboard();
        _opening.Children.Add(movement);
        _opening.Children.Add(fade);
        _opening.Completed += (_, _) => Page.Opacity = 1;
        _opening.Begin();
        Animated = true;
    }

    // ---- content ------------------------------------------------------------------------------------------------

    private void OnModelChanged() => _ui.Post(() =>
    {
        if (!_closing)
        {
            Rebuild();
            ContentRebuilt?.Invoke();
        }
    });

    private void Rebuild()
    {
        var model = _model;
        var palette = _palette;

        // Keep the keyboard on the same card's Join button when the cards are drawn again (the minute tick redraws them).
        var focusedEvent = Root.XamlRoot is { } xamlRoot && FocusManager.GetFocusedElement(xamlRoot) is DependencyObject focused
            ? EventIdOf(focused)
            : null;

        AutomationProperties.SetName(SettingsButton, _text.GetString("Flyout_Settings"));
        WeekdayText.Text = model.Weekday;
        WeekdayText.Foreground = palette.Primary;
        SummaryText.Text = model.Summary;
        SummaryText.Foreground = palette.Secondary;
        NoMoreText.Text = model.NoMoreMeetingsText;
        NoMoreText.Foreground = palette.Secondary;
        EmptyTitle.Text = model.EmptyTitle;
        EmptyTitle.Foreground = palette.Primary;
        EmptySubtitle.Text = model.EmptySubtitle;
        EmptySubtitle.Foreground = palette.Secondary;
        FooterText.Text = model.Footer;
        FooterText.Foreground = palette.Secondary;

        ProblemsPanel.Children.Clear();
        foreach (var problem in model.Problems)
        {
            ProblemsPanel.Children.Add(new ProblemRowView(problem, palette, OnProblemAction));
        }

        ProblemsPanel.Visibility = model.Problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetName(ProblemsPanel, _text.GetString("Flyout_ProblemsList"));

        Cards.Children.Clear();
        foreach (var row in model.Rows)
        {
            Cards.Children.Add(new EventCard(row, palette, _text, OnJoin));
        }

        AutomationProperties.SetName(Cards, _text.GetString("Flyout_AgendaList"));
        ListScroll.Visibility = model.Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        NoMorePanel.Visibility = model.NoMoreMeetings ? Visibility.Visible : Visibility.Collapsed;

        if (focusedEvent is not null && Cards.Children.OfType<EventCard>().FirstOrDefault(c => c.Row.EventId == focusedEvent)?.Join is { } restore)
        {
            restore.Focus(FocusState.Programmatic);
        }
    }

    private static string? EventIdOf(DependencyObject element)
    {
        for (var node = element; node is not null; node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is EventCard card)
            {
                return card.Row.EventId;
            }
        }

        return null;
    }

    /// <summary>The cards, in order (for the self-test).</summary>
    public IReadOnlyList<EventCard> EventCards => [.. Cards.Children.OfType<EventCard>()];

    /// <summary>The problem rows, in order (for the self-test).</summary>
    public IReadOnlyList<ProblemRowView> ProblemRows => [.. ProblemsPanel.Children.OfType<ProblemRowView>()];

    public Button SettingsEntry => SettingsButton;

    public FrameworkElement RootElement => Root;

    public FrameworkElement HeaderElement => Header;

    public ScrollViewer List => ListScroll;

    public bool EmptyStateShown => EmptyPanel.Visibility == Visibility.Visible;

    public bool NoMoreShown => NoMorePanel.Visibility == Visibility.Visible;

    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsPressed?.Invoke();

    private void OnJoin(string eventId) => _ = JoinAsync(eventId);

    private async Task JoinAsync(string eventId)
    {
        await _model.JoinAsync(eventId);
        // A link that opened has done its job and the flyout gets out of the way; one that did not open stays, showing the link to copy.
        if (!_closing && _model.Rows.FirstOrDefault(r => r.EventId == eventId) is { HasJoinFailed: false })
        {
            CloseFlyout();
        }
    }

    private void OnProblemAction(ProblemRow row)
    {
        if (row.ConnectionId is { } connection)
        {
            _ = _model.ReconnectAsync(connection).AsTask();
        }
    }

    // ---- keys, focus and activation ------------------------------------------------------------------------------

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (TugWindowChrome.IsForeground(Handle))
        {
            _everActive = true;
        }

        // The first Join button takes the keyboard (the gear in the header stays one Shift+Tab away); with no Join, the first control does.
        // It is not drawn as focused until a key is used, so Esc and Tab work at once.
        var firstJoin = Cards.Children.OfType<EventCard>().Select(c => c.Join).FirstOrDefault(j => j is not null);
        if (firstJoin is not null)
        {
            firstJoin.Focus(FocusState.Programmatic);
        }
        else if (FocusManager.FindFirstFocusableElement(Root) is Control first)
        {
            first.Focus(FocusState.Programmatic);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            EscapeRequested?.Invoke();
        }
    }

    /// <summary>Activation changes seen so far, newest last (for diagnosing focus problems in the self-test).</summary>
    public List<string> ActivationLog { get; } = [];

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        ActivationLog.Add($"{args.WindowActivationState} fg={TugWindowChrome.IsForeground(Handle)} closing={_closing} foreground={FlyoutWindowChrome.DescribeForeground(Handle)}");
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // Only a window that really was the foreground window can lose it; and a window being closed by us is not "the person left".
            if (_everActive && !_closing)
            {
#if SELFTEST
                // On the VM, Parallels' own foreground helper (prl_cc_fgproxy) takes the keyboard whenever the scripts that drive the VM
                // run a command, which would close the flyout under a scenario. Scenarios set this and press the "lost the active state"
                // path with LoseActivation instead.
                if (IgnoreSystemDeactivation)
                {
                    return;
                }
#endif
                WindowDeactivated?.Invoke();
            }

            return;
        }

        // Windows can refuse to hand the keyboard to a window that was not asked for by a click or a key (the foreground lock).
        // Such a window was never "left" by the person, so only a window that really was the foreground window closes on deactivation.
        if (TugWindowChrome.IsForeground(Handle))
        {
            _everActive = true;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        ActivationLog.Add("closed");
        _closing = true;
        _model.Changed -= OnModelChanged;
        _opening?.Stop();
        WindowClosed?.Invoke();
    }

#if SELFTEST
    /// <summary>True in the self-test scenarios: real deactivations are ignored (see <see cref="OnActivated"/>).</summary>
    internal static bool IgnoreSystemDeactivation { get; set; }

    /// <summary>The self-test presses Esc without a keyboard: the same path the key event takes.</summary>
    internal void PressEscape() => EscapeRequested?.Invoke();

    /// <summary>The self-test lets the window lose the active state without another program: the same path the activation event takes.</summary>
    internal void LoseActivation()
    {
        if (!_closing)
        {
            WindowDeactivated?.Invoke();
        }
    }
#endif
}
