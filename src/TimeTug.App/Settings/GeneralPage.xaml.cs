using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Settings;

/// <summary>
/// The General pane: a hub with a row for each sub-page (About, Software Update) and, on a sub-page, a back button and the page's
/// title, as on the Mac. The hub's controls (launch at login, the shortcut) and the sub-pages' contents come in later steps.
/// </summary>
public sealed partial class GeneralPage : UserControl
{
    private readonly SettingsShellViewModel _shell;
    private readonly SettingsText _text;
    private readonly ILocalizer _localizer;
    private readonly Action _highlightAgain;

    public GeneralPage(SettingsShellViewModel shell, SettingsText text, ILocalizer localizer, Action highlightAgain)
    {
        _shell = shell;
        _text = text;
        _localizer = localizer;
        _highlightAgain = highlightAgain;
        InitializeComponent();
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
            Rebuild();
        };
        Unloaded += (_, _) => shell.PropertyChanged -= OnShellChanged;
        Rebuild();
    }

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
