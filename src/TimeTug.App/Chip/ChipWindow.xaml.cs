using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Flyout;
using TimeTug.App.Theming;
using TimeTug.Platform.Chip;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Chip;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using Windows.Graphics;
using WinRT.Interop;

namespace TimeTug.App.Chip;

/// <summary>
/// The countdown chip's window: borderless, always on top, rounded, a tool window that is never activated (<see cref="NoActivateGuard"/>: the style alone does not
/// stop WinUI activating it on a press), with no taskbar button and no Alt+Tab entry. It shows what <see cref="ChipPlan"/> says and nothing else, follows the
/// person's theme and backdrop choice (acrylic through <see cref="ChipBackdrop"/>, solid under reduced transparency and under a contrast theme, where the system
/// colours apply), and has no animation. Input is pointer events only (it never has the keyboard): a press and release without movement is a click, a press
/// that moves more than a few pixels is a drag (reported on release), a double click asks for the default place, a right click opens the menu. The accessibility
/// tree holds one element, the root, named with the whole sentence; announcements are UI Automation notifications.
/// </summary>
internal sealed partial class ChipWindow : Window, IChipWindow
{
    /// <summary>How far, in physical pixels, a press has to move before it is a drag and not a click.</summary>
    private const int DragThreshold = 4;

    private readonly IUiSettings _ui;
    private readonly IPresentationSettingsStore _store;
    private readonly ILocalizer _text;
    private readonly IUiDispatcher _dispatcher;
    private readonly IDisposable _guard;
    private readonly nint _handle;
    private ChipPlan _plan = ChipPlan.Hidden;
    private bool _dark;
    private bool _contrast;
    private bool _disposed;
    private bool _pressed;
    private (int X, int Y) _pressCursor;
    private PixelRect _pressBounds;

    public ChipWindow(IUiSettings ui, IPresentationSettingsStore store, ILocalizer text, IUiDispatcher dispatcher)
    {
        _ui = ui;
        _store = store;
        _text = text;
        _dispatcher = dispatcher;
        InitializeComponent();
        Title = text.GetString("Chip_WindowTitle");
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        AppWindow.IsShownInSwitchers = false;
        _handle = WindowNative.GetWindowHandle(this);
        ChipWindowChrome.Style(_handle);
        _guard = NoActivateGuard.Protect(_handle);

        // Each window loads its own copy of the app picture (a brush shared between windows came out empty in the second one).
        Icon.Background = new ImageBrush
        {
            ImageSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/tug-icon.png")) { DecodePixelWidth = 72 },
            Stretch = Stretch.UniformToFill,
        };
        Root.PointerPressed += OnPointerPressed;
        Root.PointerMoved += OnPointerMoved;
        Root.PointerReleased += OnPointerReleased;
        Root.PointerCaptureLost += OnPointerCaptureLost;
        Root.DoubleTapped += OnDoubleTapped;
        Root.RightTapped += OnRightTapped;
        Activated += (_, _) => Activations++;
        base.Closed += OnWindowClosed;
        _store.Changed += OnLookSignal;
        _ui.Changed += OnLookSignal;
        ApplyLook();
    }

    public event Action? Clicked;

    public event Action? JoinRequested;

    public event Action? HideRequested;

    public event Action? SettingsRequested;

    public event Action<PixelRect>? Dragged;

    public event Action? ResetRequested;

    private event Action? ClosedCore;

    event Action? IChipWindow.Closed
    {
        add => ClosedCore += value;
        remove => ClosedCore -= value;
    }

    public bool IsDragging { get; private set; }

    /// <summary>The window handle (for the self-test).</summary>
    internal nint Handle => _handle;

    /// <summary>What the window shows now (for the self-test).</summary>
    internal ChipPlan Plan => _plan;

    internal FrameworkElement RootElement => Root;

    /// <summary>The backdrop in use now: Material (acrylic) or Solid.</summary>
    internal FlyoutBackdrop Backdrop { get; private set; } = FlyoutBackdrop.Solid;

    internal bool IsDark => _dark;

    internal bool IsContrast => _contrast;

    /// <summary>The sentences handed to a screen reader so far (for the self-test).</summary>
    internal List<string> Announced { get; } = [];

    /// <summary>The items of the menu opened by the last right click (for the self-test); empty before one.</summary>
    internal IReadOnlyList<MenuFlyoutItem> LastMenuItems { get; private set; } = [];

    /// <summary>How many menus a right click has opened (for the self-test).</summary>
    internal int MenusOpened { get; private set; }

    /// <summary>Times the window was activated or took the keyboard in spite of the guard (for the self-test; it must stay 0).</summary>
    internal int Activations { get; private set; }

    // ---- content ------------------------------------------------------------------------------------------------

    public SizeInDips MeasureContent()
    {
        CountdownText.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        var fixedWidth = AccentBar.Margin.Left + AccentBar.Width + Icon.Margin.Left + Icon.Width + Icon.Margin.Right
            + CountdownText.Margin.Left + CountdownText.DesiredSize.Width + CountdownText.Margin.Right;
        TitleText.MaxWidth = Math.Max(40, ChipPlacement.MaxWidthDips - fixedWidth);
        Root.Measure(new Windows.Foundation.Size(double.PositiveInfinity, ChipPlacement.HeightDips));
        var width = Math.Clamp(Math.Ceiling(Root.DesiredSize.Width), ChipPlacement.MinWidthDips, ChipPlacement.MaxWidthDips);
        return new SizeInDips(width, ChipPlacement.HeightDips);
    }

    public void SetContent(ChipPlan plan)
    {
        _plan = plan;
        TitleText.Text = plan.Title;
        CountdownText.Text = plan.Countdown;
        AutomationProperties.SetName(Root, plan.AutomationName);
        ApplyAccent();
    }

    public void Show(PixelRect bounds)
    {
        AppWindow.MoveAndResize(new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height));
        AppWindow.Show(false);
        ChipWindowChrome.Place(_handle, bounds);
    }

    public void Move(PixelRect bounds) => ChipWindowChrome.Place(_handle, bounds);

    public void Hide() => AppWindow.Hide();

    public void Announce(string text)
    {
        Announced.Add(text);
        var peer = FrameworkElementAutomationPeer.FromElement(Root) ?? FrameworkElementAutomationPeer.CreatePeerForElement(Root);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.Other, AutomationNotificationProcessing.MostRecent, text, "TimeTugChipAnnouncement");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnLookSignal;
        _ui.Changed -= OnLookSignal;
        _guard.Dispose();
        try
        {
            Close();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Already closed by the system.
        }
    }

    // ---- look ---------------------------------------------------------------------------------------------------

    private void OnLookSignal() => _dispatcher.Post(() =>
    {
        if (!_disposed)
        {
            ApplyLook();
        }
    });

    /// <summary>The person's theme and backdrop (Appearance pane), with reduced transparency and contrast themes always giving the solid one.</summary>
    private void ApplyLook()
    {
        var chosen = _store.Load();
        var dark = AppearanceResolver.IsDark(chosen.Theme, _ui);
        var contrast = _ui.HighContrast;
        var themeChanged = dark != _dark;
        _dark = dark;
        _contrast = contrast;
        Root.RequestedTheme = contrast ? ElementTheme.Default : dark ? ElementTheme.Dark : ElementTheme.Light;
        var palette = FlyoutPalette.For(dark, contrast);
        var acrylic = AppearanceResolver.Backdrop(chosen.Backdrop, _ui) == FlyoutBackdrop.Material && ChipBackdrop.IsSupported;
        if (acrylic)
        {
            if (SystemBackdrop is not ChipBackdrop existing || existing.Dark != dark || themeChanged)
            {
                SystemBackdrop = new ChipBackdrop(dark);
            }

            // A fully transparent brush still takes hits, so a click anywhere in the window reaches it.
            Root.Background = new SolidColorBrush(Colors.Transparent);
        }
        else
        {
            SystemBackdrop = null;
            Root.Background = contrast ? palette.CardFill : new SolidColorBrush(ThemeColors.Get("SolidBackgroundFillColorBase", dark));
        }

        Backdrop = acrylic ? FlyoutBackdrop.Material : FlyoutBackdrop.Solid;
        TitleText.Foreground = palette.Primary;
        CountdownText.Foreground = palette.Secondary;
        ApplyAccent();
    }

    /// <summary>The thin bar in the meeting's calendar colour (legible by the same rule as the flyout's cards), or the accent colour without one; the system's highlight colour under a contrast theme.</summary>
    private void ApplyAccent()
    {
        var palette = FlyoutPalette.For(_dark, _contrast);
        if (_contrast || _plan.AccentColorHex is null)
        {
            AccentBar.Background = palette.Blue;
            AccentBar.BorderThickness = new Thickness(0);
            return;
        }

        var swatch = SwatchColors.For(_plan.AccentColorHex, _dark);
        AccentBar.Background = SwatchColors.Brush(swatch.Fill);
        AccentBar.BorderBrush = SwatchColors.Brush(swatch.Edge);
        AccentBar.BorderThickness = swatch.FillShows ? new Thickness(0) : new Thickness(1);
    }

    // ---- pointer ------------------------------------------------------------------------------------------------

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = true;
        IsDragging = false;
        _pressCursor = ChipWindowChrome.CursorPosition();
        _pressBounds = ChipWindowChrome.OuterBounds(_handle) ?? default;
        _ = Root.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var (x, y) = ChipWindowChrome.CursorPosition();
        var (dx, dy) = (x - _pressCursor.X, y - _pressCursor.Y);
        if (!IsDragging && Math.Abs(dx) < DragThreshold && Math.Abs(dy) < DragThreshold)
        {
            return;
        }

        IsDragging = true;
        AppWindow.Move(new PointInt32(_pressBounds.X + dx, _pressBounds.Y + dy));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        var dragged = IsDragging;
        _pressed = false;
        IsDragging = false;
        Root.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
        if (dragged)
        {
            if (ChipWindowChrome.OuterBounds(_handle) is { } dropped)
            {
                Dragged?.Invoke(dropped);
            }
        }
        else
        {
            Clicked?.Invoke();
        }
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // Capture taken away (a menu opened, the system took it): a drag in progress ends where it is, a click in progress is abandoned.
        if (_pressed && IsDragging && ChipWindowChrome.OuterBounds(_handle) is { } dropped)
        {
            Dragged?.Invoke(dropped);
        }

        _pressed = false;
        IsDragging = false;
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ResetRequested?.Invoke();
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ShowMenu(e.GetPosition(Root));
    }

    private void ShowMenu(Windows.Foundation.Point at)
    {
        var items = new List<MenuFlyoutItem>();
        if (_plan.CanJoin)
        {
            items.Add(Item("Chip_Menu_Join", () => JoinRequested?.Invoke()));
        }

        items.Add(Item("Chip_Menu_Hide", () => HideRequested?.Invoke()));
        items.Add(Item("Chip_Menu_Settings", () => SettingsRequested?.Invoke()));
        var menu = new MenuFlyout();
        foreach (var item in items)
        {
            menu.Items.Add(item);
        }

        LastMenuItems = items;
        MenusOpened++;
        menu.ShowAt(Root, new FlyoutShowOptions { Position = at, ShowMode = FlyoutShowMode.Transient });
    }

    private MenuFlyoutItem Item(string key, Action action)
    {
        var item = new MenuFlyoutItem { Text = _text.GetString(key) };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _guard.Dispose();
        ClosedCore?.Invoke();
    }
}
