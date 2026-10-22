using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Settings;

/// <summary>
/// The General pane: a hub with a row for each sub-page (About, Software Update), then the App section (launch at login) and the Shortcut
/// section (the recorder), in the Mac's order; on a sub-page, a back button and the page's title, as on the Mac. The sub-pages' contents come in a later step.
/// </summary>
public sealed partial class GeneralPage : UserControl
{
    private readonly SettingsShellViewModel _shell;
    private readonly SettingsText _text;
    private readonly ILocalizer _localizer;
    private readonly GeneralViewModel _model;
    private readonly Action _highlightAgain;
    private bool _syncing;

    public GeneralPage(SettingsShellViewModel shell, SettingsText text, ILocalizer localizer, GeneralViewModel model, Action highlightAgain)
    {
        _shell = shell;
        _text = text;
        _localizer = localizer;
        _model = model;
        _highlightAgain = highlightAgain;
        InitializeComponent();
        AppHeader.Text = localizer.GetString("General_SectionApp");
        LaunchCard.Header = text.ControlTitle(GeneralViewModel.Ids.LaunchAtLogin);
        AutomationProperties.SetName(LaunchCard, text.ControlTitle(GeneralViewModel.Ids.LaunchAtLogin));
        AutomationProperties.SetName(LaunchSwitch, text.ControlTitle(GeneralViewModel.Ids.LaunchAtLogin));
        LaunchLink.Content = model.LaunchAtLogin.SettingsLinkText;
        AutomationProperties.SetName(LaunchLink, model.LaunchAtLogin.SettingsLinkAutomationName);
        SettingsHighlight.SetId(LaunchHost, GeneralViewModel.Ids.LaunchAtLogin);

        var recorder = model.Recorder;
        ShortcutHeader.Text = localizer.GetString("General_SectionShortcut");
        ShortcutCard.Header = recorder.Label;
        ShortcutCard.Description = recorder.Description;
        AutomationProperties.SetName(ShortcutCard, recorder.Label);
        RecorderControl = new HotkeyRecorder(recorder);
        RecorderHost.Content = RecorderControl;
        SettingsHighlight.SetId(ShortcutHost, GeneralViewModel.Ids.PopupShortcut);
        BackText.Text = localizer.GetString("Settings_Back");
        AutomationProperties.SetName(BackButton, localizer.GetString("Settings_BackAutomation"));
        AboutCard.Header = localizer.GetString("Settings_Destination_About");
        AutomationProperties.SetName(AboutCard, localizer.GetString("Settings_Destination_About"));
        UpdateCard.Header = localizer.GetString("Settings_Destination_SoftwareUpdate");
        AutomationProperties.SetName(UpdateCard, localizer.GetString("Settings_Destination_SoftwareUpdate"));
        // The page is unloaded while another pane is open and loaded again when General comes back: listen only while it is on screen.
        Loaded += (_, _) =>
        {
            shell.PropertyChanged += OnShellChanged;
            model.LaunchAtLogin.PropertyChanged += OnLaunchChanged;
            model.Recorder.PropertyChanged += OnRecorderChanged;
            Rebuild();
            SyncLaunch();
            SyncShortcut();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            shell.PropertyChanged -= OnShellChanged;
            model.LaunchAtLogin.PropertyChanged -= OnLaunchChanged;
            model.Recorder.PropertyChanged -= OnRecorderChanged;
        };
        Rebuild();
        SyncLaunch();
        SyncShortcut();
    }

    /// <summary>The shortcut recorder (for the self-test).</summary>
    internal HotkeyRecorder RecorderControl { get; }

    internal TextBlock ShortcutStatusText => ShortcutStatus;

    internal FontIcon ShortcutIcon => ShortcutStatusIcon;

    internal StackPanel ShortcutStatusRow => ShortcutStatusHost;

    /// <summary>Ends a recording in progress (the window lost the keyboard) so the shortcut is registered again.</summary>
    public void CancelRecording() => _ = _model.Recorder.CancelRecordingAsync();

    private void OnRecorderChanged(object? sender, PropertyChangedEventArgs e) => SyncShortcut();

    /// <summary>The line under the shortcut row: active, a conflict (also one found at the last start), or what was wrong with the keys.</summary>
    private void SyncShortcut()
    {
        var recorder = _model.Recorder;
        ShortcutStatus.Text = recorder.StatusText ?? string.Empty;
        ShortcutStatusHost.Visibility = recorder.HasStatus ? Visibility.Visible : Visibility.Collapsed;
        ShortcutStatusIcon.Visibility = recorder.IsProblem ? Visibility.Visible : Visibility.Collapsed;
        ShortcutStatus.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[recorder.IsProblem ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush"];
    }

    /// <summary>The launch-at-login switch and its note and link (for the self-test).</summary>
    internal ToggleSwitch LaunchToggle => LaunchSwitch;

    internal TextBlock LaunchNoteText => LaunchNote;

    internal HyperlinkButton LaunchSettingsLink => LaunchLink;

    internal TextBlock LaunchErrorText => LaunchError;

    /// <summary>Reads what Windows says about launch at login again. Called when the page appears and whenever the window becomes active, since the person may have changed it in Windows Settings meanwhile.</summary>
    public void Refresh() => _ = _model.LaunchAtLogin.RefreshAsync();

    private void OnLaunchChanged(object? sender, PropertyChangedEventArgs e) => SyncLaunch();

    /// <summary>Puts the switch, the note, the link and the error to the view model's values without raising the person's-change handler.</summary>
    private void SyncLaunch()
    {
        var launch = _model.LaunchAtLogin;
        _syncing = true;
        try
        {
            LaunchSwitch.IsOn = launch.IsOn;
            LaunchSwitch.IsEnabled = launch.CanChange;
            LaunchNote.Text = launch.Note ?? string.Empty;
            LaunchLink.Visibility = launch.ShowSettingsLink ? Visibility.Visible : Visibility.Collapsed;
            LaunchNoteHost.Visibility = launch.HasNote ? Visibility.Visible : Visibility.Collapsed;
            LaunchError.Text = launch.Error ?? string.Empty;
            LaunchError.Visibility = launch.HasError ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void OnLaunchToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        await _model.LaunchAtLogin.SetAsync(LaunchSwitch.IsOn);
        // The switch shows what Windows says, which can differ from what the person asked for.
        SyncLaunch();
    }

    private async void OnLaunchLink(object sender, RoutedEventArgs e) => await _model.LaunchAtLogin.OpenStartupSettingsAsync();

    /// <summary>The back button, shown on a sub-page (for the self-test).</summary>
    public Button Back => BackButton;

    /// <summary>The hub's rows, shown when no sub-page is open (for the self-test).</summary>
    public StackPanel HubPanel => Hub;

    public TextBlock Title => Heading;

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsShellViewModel.GeneralPath))
        {
            Rebuild();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => _highlightAgain());
        }
    }

    private void Rebuild()
    {
        var destination = _shell.Destination;
        BackButton.Visibility = destination is null ? Visibility.Collapsed : Visibility.Visible;
        Hub.Visibility = destination is null ? Visibility.Visible : Visibility.Collapsed;
        Heading.Text = destination switch
        {
            GeneralDestination.About => _localizer.GetString("Settings_Destination_About"),
            GeneralDestination.SoftwareUpdate => _localizer.GetString("Settings_Destination_SoftwareUpdate"),
            _ => _text.PaneTitle(SettingsPane.General),
        };
        // The About page is what the search's "About TimeTug" entry reveals.
        SettingsHighlight.SetId(HeadingHost, destination == GeneralDestination.About ? "about" : null);
    }

    private void OnBack(object sender, RoutedEventArgs e) => _shell.Back();

    private void OnAbout(object sender, RoutedEventArgs e) => _shell.OpenDestination(GeneralDestination.About);

    private void OnUpdate(object sender, RoutedEventArgs e) => _shell.OpenDestination(GeneralDestination.SoftwareUpdate);
}
