using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Settings;

/// <summary>The About page of the General pane (the Mac's About content, with the package version, a copyright line and links to the website and the licence).</summary>
public sealed partial class AboutPage : UserControl
{
    private readonly AboutViewModel _model;

    public AboutPage(AboutViewModel model)
    {
        _model = model;
        InitializeComponent();
        NameText.Text = model.Name;
        AutomationProperties.SetName(Lockup, model.LockupAutomationName);
        TaglineText.Text = model.Tagline;
        VersionText.Text = model.VersionText;
        CreditText.Text = model.CreditText;
        CopyrightText.Text = model.CopyrightText;
        LicenceText.Text = model.LicenceNote;
        WebsiteLink.Content = model.WebsiteText;
        AutomationProperties.SetName(WebsiteLink, model.WebsiteAutomationName);
        LicenceLink.Content = model.LicencesText;
        AutomationProperties.SetName(LicenceLink, model.LicencesAutomationName);
        Loaded += (_, _) => model.PropertyChanged += OnModelChanged;
        Unloaded += (_, _) => model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>The page's texts and links (for the self-test).</summary>
    internal TextBlock Version => VersionText;

    internal TextBlock Credit => CreditText;

    internal TextBlock Copyright => CopyrightText;

    internal TextBlock Tagline => TaglineText;

    internal TextBlock Licence => LicenceText;

    internal HyperlinkButton Website => WebsiteLink;

    internal HyperlinkButton Licences => LicenceLink;

    internal TextBlock Error => ErrorText;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        ErrorText.Text = _model.Error ?? string.Empty;
        ErrorText.Visibility = _model.HasError ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnWebsite(object sender, RoutedEventArgs e) => await _model.OpenWebsiteAsync();

    private async void OnLicence(object sender, RoutedEventArgs e) => await _model.OpenLicenceAsync();
}
