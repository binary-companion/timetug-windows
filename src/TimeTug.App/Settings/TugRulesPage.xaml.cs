using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Settings;

/// <summary>
/// The Tug Rules pane: Enable Tug with its hero text, the lead time, the two requirements (each with its warning) and the Test button.
/// It shows the view model's values and passes the person's changes to it; when the engine refuses a change the view model raises the
/// value again and the control goes back to it.
/// </summary>
public sealed partial class TugRulesPage : UserControl
{
    private readonly TugRulesViewModel _model;
    private bool _syncing;

    public TugRulesPage(TugRulesViewModel model, SettingsText text, ILocalizer localizer, string title)
    {
        _model = model;
        InitializeComponent();
        Heading.Text = title;
        TimingHeader.Text = localizer.GetString("TugRules_SectionTiming");
        RequirementsHeader.Text = localizer.GetString("TugRules_SectionRequirements");
        TestHeader.Text = localizer.GetString("TugRules_SectionTest");
        HeroTitle.Text = model.HeroTitle;
        AutomationProperties.SetName(EnableSwitch, text.ControlTitle(TugRulesViewModel.Ids.EnableTug));

        LeadTimeCard.Header = text.ControlTitle(TugRulesViewModel.Ids.LeadTime);
        LeadTimeCard.Description = model.LeadTimeCaption;
        AutomationProperties.SetName(LeadTimeCard, text.ControlTitle(TugRulesViewModel.Ids.LeadTime));
        AutomationProperties.SetName(LeadTimeBox, text.ControlTitle(TugRulesViewModel.Ids.LeadTime));

        LinkCard.Header = text.ControlTitle(TugRulesViewModel.Ids.VideoLink);
        AutomationProperties.SetName(LinkCard, text.ControlTitle(TugRulesViewModel.Ids.VideoLink));
        AutomationProperties.SetName(LinkSwitch, text.ControlTitle(TugRulesViewModel.Ids.VideoLink));
        LinkWarning.Text = model.LinkWarning;
        AttendeesCard.Header = text.ControlTitle(TugRulesViewModel.Ids.RequireAttendees);
        AutomationProperties.SetName(AttendeesCard, text.ControlTitle(TugRulesViewModel.Ids.RequireAttendees));
        AutomationProperties.SetName(AttendeesSwitch, text.ControlTitle(TugRulesViewModel.Ids.RequireAttendees));
        AttendeesWarning.Text = model.AttendeesWarning;

        TestCard.Header = text.ControlTitle(TugRulesViewModel.Ids.TestTug);
        TestCard.Description = localizer.GetString("TugRules_TestDescription");
        AutomationProperties.SetName(TestCard, text.ControlTitle(TugRulesViewModel.Ids.TestTug));
        TestButton.Content = localizer.GetString("TugRules_TestButton");
        AutomationProperties.SetName(TestButton, text.ControlTitle(TugRulesViewModel.Ids.TestTug));

        SettingsHighlight.SetId(EnableHost, TugRulesViewModel.Ids.EnableTug);
        SettingsHighlight.SetId(LeadTimeHost, TugRulesViewModel.Ids.LeadTime);
        SettingsHighlight.SetId(LinkHost, TugRulesViewModel.Ids.VideoLink);
        SettingsHighlight.SetId(LinkWarningHost, TugRulesViewModel.Ids.LinkWarning);
        SettingsHighlight.SetId(AttendeesHost, TugRulesViewModel.Ids.RequireAttendees);
        SettingsHighlight.SetId(AttendeesWarningHost, TugRulesViewModel.Ids.AttendeesWarning);
        SettingsHighlight.SetId(TestHost, TugRulesViewModel.Ids.TestTug);

        // Listen only while the page is on screen (the window keeps the page when another pane is open).
        Loaded += (_, _) =>
        {
            model.PropertyChanged += OnModelChanged;
            Sync();
        };
        Unloaded += (_, _) => model.PropertyChanged -= OnModelChanged;
        Sync();
    }

    /// <summary>The controls (for the self-test).</summary>
    internal ToggleSwitch EnableToggle => EnableSwitch;

    internal ComboBox LeadTime => LeadTimeBox;

    internal ToggleSwitch LinkToggle => LinkSwitch;

    internal ToggleSwitch AttendeesToggle => AttendeesSwitch;

    internal Button Test => TestButton;

    internal ScrollViewer ScrollArea => Scroller;

    internal TextBlock HeroSubtitleText => HeroSubtitle;

    internal Grid LinkWarningRow => LinkWarningHost;

    internal Grid AttendeesWarningRow => AttendeesWarningHost;

    internal InfoBar Message => MessageBar;

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    /// <summary>Puts every control to the view model's values without raising the person's-change handlers.</summary>
    private void Sync()
    {
        _syncing = true;
        try
        {
            EnableSwitch.IsOn = _model.Enabled;
            HeroSubtitle.Text = _model.HeroSubtitle;
            if (!ReferenceEquals(LeadTimeBox.ItemsSource, _model.LeadTimeOptions))
            {
                LeadTimeBox.ItemsSource = _model.LeadTimeOptions;
            }

            LeadTimeBox.SelectedItem = _model.SelectedLeadTime;
            LinkSwitch.IsOn = _model.RequireConferenceLink;
            AttendeesSwitch.IsOn = _model.RequireOtherAttendees;
            LinkWarningHost.Visibility = _model.ShowLinkWarning ? Visibility.Visible : Visibility.Collapsed;
            AttendeesWarningHost.Visibility = _model.ShowAttendeesWarning ? Visibility.Visible : Visibility.Collapsed;
            MessageBar.Title = _model.Message ?? string.Empty;
            MessageBar.IsOpen = _model.HasMessage;
        }
        finally
        {
            _syncing = false;
        }
    }

    private async void OnEnableToggled(object sender, RoutedEventArgs e) => await ChangeAsync(() => _model.SetEnabledAsync(EnableSwitch.IsOn));

    private async void OnLeadTimeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LeadTimeBox.SelectedItem is LeadTimeOption option)
        {
            await ChangeAsync(() => _model.SetLeadMinutesAsync(option.Minutes));
        }
    }

    private async void OnLinkToggled(object sender, RoutedEventArgs e) => await ChangeAsync(() => _model.SetRequireConferenceLinkAsync(LinkSwitch.IsOn));

    private async void OnAttendeesToggled(object sender, RoutedEventArgs e) => await ChangeAsync(() => _model.SetRequireOtherAttendeesAsync(AttendeesSwitch.IsOn));

    private async void OnTest(object sender, RoutedEventArgs e) => await ChangeAsync(_model.TestTugAsync, evenWhileSyncing: true);

    /// <summary>Runs a change the person made. A switch or choice set from the view model's own values (<see cref="Sync"/>) is not a change.</summary>
    private async Task ChangeAsync(Func<Task> change, bool evenWhileSyncing = false)
    {
        if (_syncing && !evenWhileSyncing)
        {
            return;
        }

        try
        {
            await change();
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            // The window is closing; there is nobody left to tell.
        }
    }
}
