using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Settings;

/// <summary>The Software Update page of the General pane: the version, and where updates come from (the Microsoft Store; a development build shows its commit).</summary>
public sealed partial class SoftwareUpdatePage : UserControl
{
    private readonly AboutViewModel _model;

    public SoftwareUpdatePage(AboutViewModel model)
    {
        _model = model;
        InitializeComponent();
        Card.Header = model.UpdateTitle;
        Card.Description = model.UpdateStatusText;
        AutomationProperties.SetName(Card, model.UpdateTitle + ". " + model.UpdateStatusText);
        StoreButton.Content = model.StoreLinkText;
        AutomationProperties.SetName(StoreButton, model.StoreLinkAutomationName);
        StoreButton.Visibility = model.ShowStoreLink ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) => model.PropertyChanged += OnModelChanged;
        Unloaded += (_, _) => model.PropertyChanged -= OnModelChanged;
    }

    /// <summary>The card and its button (for the self-test).</summary>
    internal CommunityToolkit.WinUI.Controls.SettingsCard UpdateCard => Card;

    internal Button StoreLink => StoreButton;

    internal TextBlock Error => ErrorText;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        ErrorText.Text = _model.Error ?? string.Empty;
        ErrorText.Visibility = _model.HasError ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnStore(object sender, RoutedEventArgs e) => await _model.OpenStoreUpdatesAsync();
}
