using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using TimeTug.App.Controls;
using TimeTug.App.Theming;
using TimeTug.Platform.Broker;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Welcome;
using Windows.Graphics;

namespace TimeTug.App.Views;

/// <summary>
/// The first-run window: a normal centred window with its own taskbar button (not attached to the tray), Mica behind it.
/// Everything it shows comes from <see cref="WelcomeViewModel"/>; this file only places the window and moves focus.
/// </summary>
public sealed partial class WelcomeWindow : Window
{
    private const int WidthInDips = 560;
    private const int HeightInDips = 700;
    private readonly AccountFormService _forms;
    private readonly IDisposable? _theme;
    private readonly IUiSettings? _uiSettings;
    private string? _guideAsset;
    private bool _syncing;

    private readonly BrokerParentWindow? _brokerParent;
    private readonly nint _handle;

    /// <param name="brokerParent">Where this window registers itself while open, so the Windows sign-in dialog opens in front of it.</param>
    /// <param name="uiSettings">Tells whether Windows has animations on; the taskbar guide on the last step plays only then and is a still picture otherwise. Without it the guide plays.</param>
    public WelcomeWindow(
        WelcomeViewModel viewModel, AccountFormService forms, ILocalizer text, ThemeService? theme = null, BrokerParentWindow? brokerParent = null,
        IUiSettings? uiSettings = null)
    {
        ViewModel = viewModel;
        _forms = forms;
        _brokerParent = brokerParent;
        _uiSettings = uiSettings;
        InitializeComponent();
        _handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _brokerParent?.Register(_handle);
        Activated += OnActivated;
        _theme = theme?.Attach(this, Root);
        Title = text.GetString("WelcomeWindowTitle");
        ViewModel.CloseRequested += (_, _) => Close();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Closed += OnClosed;
        Root.Loaded += OnLoaded;
        if (viewModel.Taskbar is { } taskbar)
        {
            // The label wraps in a narrow window instead of being cut off.
            TaskbarButton.Content = new TextBlock { Text = taskbar.ButtonText, TextWrapping = TextWrapping.Wrap };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TaskbarButton, taskbar.ButtonAutomationName);
            taskbar.PropertyChanged += (_, _) => SyncTaskbar();
        }

        if (_uiSettings is not null)
        {
            _uiSettings.Changed += OnUiSettingsChanged;
        }

        if (viewModel.Calendars is { } calendars)
        {
            // The list Settings > Calendars shows, without the subscribed and system disclosure. It only reads the engine's state until the person changes something.
            CalendarsBox.Attach(calendars, text, uiSettings, includeSystem: false);
            calendars.PropertyChanged += (_, _) => SyncCalendarsEmpty();
            SyncCalendarsEmpty();
        }

        if (viewModel.LaunchAtLogin is { } launch)
        {
            LaunchLink.Content = launch.SettingsLinkText;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LaunchLink, launch.SettingsLinkAutomationName);
            launch.PropertyChanged += (_, _) => SyncLaunch();
            SyncLaunch();
        }
    }

    /// <summary>The calendars list of the "You're connected" step, the line shown while it is empty and the step's button (for the self-test).</summary>
    internal CalendarListView CalendarsList => CalendarsBox;

    internal TextBlock CalendarsEmptyText => CalendarsEmpty;

    internal Button ConnectedContinue => ConnectedContinueButton;

    internal Button Done => DoneButton;

    internal StackPanel ConnectedStepPanel => ConnectedPanel;

    internal StackPanel HandyStepPanel => HandyPanel;

    /// <summary>The "no calendars found yet" line stands in for the list while the engine lists none.</summary>
    private void SyncCalendarsEmpty() =>
        CalendarsEmpty.Visibility = ViewModel.Calendars is { IsEmpty: true } ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The launch-at-login checkbox, its note, link and error (for the self-test).</summary>
    internal CheckBox LaunchCheckBox => LaunchCheck;

    internal TextBlock LaunchNoteText => LaunchNote;

    internal HyperlinkButton LaunchSettingsLink => LaunchLink;

    internal TextBlock LaunchErrorText => LaunchError;

    /// <summary>Puts the checkbox, its note, link and error to the view model's values (what Windows says) without raising the person's-change handler.</summary>
    private void SyncLaunch()
    {
        if (ViewModel.LaunchAtLogin is not { } launch)
        {
            return;
        }

        _syncing = true;
        try
        {
            LaunchCheck.IsChecked = launch.IsOn;
            LaunchCheck.IsEnabled = launch.CanChange;
            LaunchNote.Text = launch.Note ?? string.Empty;
            LaunchNote.Visibility = launch.HasNote ? Visibility.Visible : Visibility.Collapsed;
            LaunchLink.Visibility = launch.ShowSettingsLink ? Visibility.Visible : Visibility.Collapsed;
            LaunchError.Text = launch.Error ?? string.Empty;
            LaunchError.Visibility = launch.HasError ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void OnLaunchChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        await ViewModel.SetLaunchAtLoginAsync(LaunchCheck.IsChecked == true);
        // The box shows what Windows says, which can differ from what the person asked for.
        SyncLaunch();
    }

    private async void OnLaunchLink(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LaunchAtLogin is { } launch)
        {
            await launch.OpenStartupSettingsAsync();
        }
    }

    public WelcomeViewModel ViewModel { get; }

    /// <summary>The taskbar guide's picture, the button that opens Windows Settings and its error line (for the self-test).</summary>
    internal Image TaskbarGuidePicture => GuideImage;

    internal Button TaskbarSettingsButton => TaskbarButton;

    internal TextBlock TaskbarErrorText => TaskbarError;

    internal Border TaskbarStepCard => TaskbarCard;

    /// <summary>The asset the guide shows now (the animation or its still), or null before the last step.</summary>
    internal string? GuideAsset => _guideAsset;

    private async void OnTaskbarSettings(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Taskbar is { } taskbar)
        {
            await taskbar.OpenAsync();
        }
    }

    private void SyncTaskbar()
    {
        var error = ViewModel.Taskbar?.Error;
        TaskbarError.Text = error ?? string.Empty;
        TaskbarError.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Shows the guide: the looping recording when Windows has animations on, a still of its last frame when they are off. Called when the last step appears (so the
    /// loop starts when the person can see it) and when the Windows setting changes meanwhile. Does nothing when the right picture is already in.
    /// </summary>
    private void ShowGuide()
    {
        var asset = TaskbarGuide.AssetFor(_uiSettings?.AnimationsEnabled ?? true);
        if (asset == _guideAsset)
        {
            return;
        }

        _guideAsset = asset;
        GuideImage.Source = new BitmapImage(new Uri("ms-appx:///" + asset));
    }

    private void OnUiSettingsChanged()
    {
        // Raised on whichever thread Windows called from.
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.IsHandyStep)
            {
                ShowGuide();
            }
        });
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _forms.XamlRoot = Root.XamlRoot;
        var scale = Root.XamlRoot.RasterizationScale;
        var size = new SizeInt32((int)(WidthInDips * scale), (int)(HeightInDips * scale));
        var work = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        size = new SizeInt32(Math.Min(size.Width, work.Width), Math.Min(size.Height, work.Height));
        AppWindow.Resize(size);
        AppWindow.Move(new PointInt32(work.X + ((work.Width - size.Width) / 2), work.Y + ((work.Height - size.Height) / 2)));
        FocusFirstControl();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WelcomeViewModel.Step))
        {
            // The new step's first control gets focus so the keyboard (and Narrator) continue where the person is looking.
            DispatcherQueue.TryEnqueue(FocusFirstControl);
            if (ViewModel.IsHandyStep)
            {
                // The loop starts when the person can see it.
                ShowGuide();
            }
        }
    }

    private void FocusFirstControl()
    {
        if (ViewModel.IsHandyStep)
        {
            DoneButton.Focus(FocusState.Programmatic);
            return;
        }

        if (ViewModel.IsConnectedStep)
        {
            ConnectedContinueButton.Focus(FocusState.Programmatic);
            return;
        }

        if (ViewModel.IsIntroStep)
        {
            GetStartedButton.Focus(FocusState.Programmatic);
            return;
        }

        var first = FocusManager.FindFirstFocusableElement(ConnectPanel) as Control;
        first?.Focus(FocusState.Programmatic);
    }

    /// <summary>The accounts list on the connect step (for the self-test).</summary>
    internal AccountsListBox AccountsList => AccountsBox;

    internal Button GetStarted => GetStartedButton;

    internal Button Continue => ContinueButton;

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _brokerParent?.Touch(_handle);
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _brokerParent?.Unregister(_handle);
        if (_uiSettings is not null)
        {
            _uiSettings.Changed -= OnUiSettingsChanged;
        }

        _forms.XamlRoot = null;
        _theme?.Dispose();
        // Closing the window mid sign-in cancels it, so the engine is not left waiting for a browser or a form.
        _ = ViewModel.Accounts.CancelAsync();
        ViewModel.Dispose();
    }
}
