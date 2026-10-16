using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media.Animation;
using TimeTug.Presentation.Tug;

namespace TimeTug.App.Tug;

/// <summary>
/// The tug on every other display: the countdown and the title, and "Dismiss on your main display". It has no controls and
/// is never activated, so it cannot take the keyboard from the main window; a click on it brings the main window forward.
/// </summary>
internal sealed partial class MirrorTugWindow : Window
{
    private readonly TugTheme _theme;
    private Storyboard? _pulse;
    private bool _closed;

    public MirrorTugWindow(TugViewModel model, TugMonitorPlan plan, Action onClicked)
    {
        ViewModel = model;
        Plan = plan;
        InitializeComponent();
        _theme = TugTheme.For(model.Palette);
        Title = model.WindowTitle;
        TugWindowSetup.ApplyBackdrop(this, Root, model, _theme);

        AppIcon.Visibility = _theme.IsContrast ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetAccessibilityView(AppIcon, AccessibilityView.Raw);
        AutomationProperties.SetAccessibilityView(CountdownLabel, AccessibilityView.Raw);
        TitleText.Text = model.Title;
        TugWindowSetup.Name(TitleText, model.Title);
        MirrorHintText.Text = model.MirrorHint;
        TugWindowSetup.Name(MirrorHintText, model.MirrorHint);
        TitleText.Foreground = _theme.Text;
        MirrorHintText.Foreground = _theme.Muted;
        CountdownLabel.Foreground = _theme.Muted;
        Render();

        model.PropertyChanged += OnModelChanged;
        Root.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => onClicked()), handledEventsToo: true);
        Closed += OnClosed;
        Root.Loaded += (_, _) => TugWindowSetup.PlaceOnMonitor(this, plan, activate: false);
        TugWindowSetup.PlaceOnMonitor(this, plan, activate: false);
    }

    public TugViewModel ViewModel { get; }

    public TugMonitorPlan Plan { get; }

    private void Render()
    {
        var model = ViewModel;
        CountdownLabel.Text = model.CountdownLabel ?? string.Empty;
        CountdownLabel.Visibility = model.HasCountdownLabel ? Visibility.Visible : Visibility.Collapsed;
        CountdownValue.Text = model.CountdownValue;
        CountdownValue.FontSize = 56;
        CountdownValue.Foreground = _theme.HeadlineBrushes[model.Tone];
        TugWindowSetup.Name(CountdownValue, model.SpokenCountdown);
        if (model.PulseEnabled && !_closed)
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

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        ViewModel.PropertyChanged -= OnModelChanged;
        TugWindowSetup.StopPulse(_pulse, CountdownValue);
    }
}
