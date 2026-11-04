using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;
using Windows.UI;

namespace TimeTug.App.Settings;

/// <summary>
/// The Calendars pane: the list of calendars (<see cref="CalendarListView"/>, the one control the first-run window shows too: an "All" switch per account
/// group, a visibility checkbox in the calendar's colour and a Tug switch per calendar, the subscribed and system calendars in a disclosure) and the options.
/// It shows the view model's values and passes the person's changes to it.
/// </summary>
public sealed partial class CalendarsPage : UserControl
{
    private readonly CalendarsViewModel _model;
    private readonly SettingsShellViewModel _shell;
    private readonly ILocalizer _text;
    private bool _syncing;
    private bool _asking;

    public CalendarsPage(CalendarsViewModel model, SettingsShellViewModel shell, SettingsText text, ILocalizer localizer, IUiSettings ui, string title)
    {
        _model = model;
        _shell = shell;
        _text = localizer;
        InitializeComponent();
        Heading.Text = title;
        Intro.Text = model.IntroText;
        EmptyText.Text = model.EmptyText;
        EmptyLink.Content = model.EmptyLinkText;
        AutomationProperties.SetName(EmptyLink, model.EmptyLinkText);
        OptionsHeader.Text = model.OptionsTitle;
        // A search result for a calendar may point at a control that was just built.
        List.Rebuilt += (_, _) => SettingsHighlight.Apply(this, _shell.HighlightedId, _shell.AnimateHighlight);
        List.Attach(model, localizer, ui, includeSystem: true);

        SkipCard.Header = text.ControlTitle(CalendarsViewModel.Ids.SkipAllDay);
        AutomationProperties.SetName(SkipCard, text.ControlTitle(CalendarsViewModel.Ids.SkipAllDay));
        AutomationProperties.SetName(SkipSwitch, text.ControlTitle(CalendarsViewModel.Ids.SkipAllDay));
        SettingsHighlight.SetId(SkipHost, CalendarsViewModel.Ids.SkipAllDay);

        var inferenceTitle = text.ControlTitle(CalendarsViewModel.Ids.Inference);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = inferenceTitle, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        var badge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1, 8, 2),
            Background = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0x9E, 0x1A)),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = model.BetaText, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
        };
        AutomationProperties.SetName(badge, model.BetaAutomationName);
        header.Children.Add(badge);
        InferenceCard.Header = header;
        AutomationProperties.SetName(InferenceCard, inferenceTitle);
        AutomationProperties.SetName(InferenceSwitch, inferenceTitle);
        InferenceStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"] };
        var description = new StackPanel { Spacing = 4 };
        description.Children.Add(new TextBlock { Text = model.InferenceExplanation, TextWrapping = TextWrapping.Wrap });
        description.Children.Add(InferenceStatus);
        InferenceCard.Description = description;
        SettingsHighlight.SetId(InferenceHost, CalendarsViewModel.Ids.Inference);
        ForgetButton.Content = model.ForgetText;
        AutomationProperties.SetName(ForgetButton, model.ForgetText);

        Loaded += (_, _) =>
        {
            model.PropertyChanged += OnModelChanged;
            shell.PropertyChanged += OnShellChanged;
            Reveal();
            Sync();
        };
        Unloaded += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            shell.PropertyChanged -= OnShellChanged;
        };
        Sync();
    }

    private TextBlock InferenceStatus { get; }

    /// <summary>The option switches and the empty state's link (for the self-test).</summary>
    internal ToggleSwitch SkipToggle => SkipSwitch;

    internal ToggleSwitch InferenceToggle => InferenceSwitch;

    internal TextBlock InferenceStatusText => InferenceStatus;

    internal HyperlinkButton Forget => ForgetButton;

    internal HyperlinkButton GoToAccounts => EmptyLink;

    internal StackPanel EmptyState => EmptyPanel;

    internal InfoBar Message => MessageBar;

    internal Expander? Disclosure => List.Disclosure;

    internal ScrollViewer ScrollArea => Scroller;

    /// <summary>The controls of the calendar with this key (for the self-test), or null.</summary>
    internal (CheckBox Show, ToggleSwitch Tug)? ControlsOf(string key) => List.ControlsOf(key);

    /// <summary>The "All" switch of the group (for the self-test).</summary>
    internal ToggleSwitch? AllSwitchOf(string sourceId, string accountName) => List.AllSwitchOf(sourceId, accountName);

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsShellViewModel.HighlightedId))
        {
            Reveal();
        }
    }

    /// <summary>A search result for a calendar in the system list opens the list first, so the calendar is there to be tinted and scrolled to.</summary>
    private void Reveal()
    {
        if (_shell.HighlightedId is { } id && id.StartsWith(CalendarsViewModel.Ids.CalendarPrefix, StringComparison.Ordinal) && _model.SystemRows.Any(r => r.HighlightId == id) && !_model.SystemExpanded)
        {
            _model.SystemExpanded = true;
            // The rows inside the disclosure only exist once it has opened and been laid out: tint and scroll to the one asked for as soon as it is there.
            TintWhenPresent(id);
        }
    }

    private void TintWhenPresent(string id)
    {
        void OnLayout(object? sender, object e)
        {
            if (_shell.HighlightedId != id)
            {
                LayoutUpdated -= OnLayout;
                return;
            }

            if (SettingsHighlight.Hosts(this).Any(h => SettingsHighlight.GetId(h) == id))
            {
                LayoutUpdated -= OnLayout;
                SettingsHighlight.Apply(this, id, _shell.AnimateHighlight);
                SettingsHighlight.BringIntoView(this, id, _shell.AnimateHighlight);
            }
        }

        LayoutUpdated += OnLayout;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    /// <summary>How the colour swatches are drawn now (for the self-test): "dark", "light" or "contrast".</summary>
    internal string SwatchLook => List.SwatchLook;

    private void Sync()
    {
        _syncing = true;
        try
        {
            var empty = _model.IsEmpty;
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            Intro.Visibility = Visibility.Visible;
            SkipSwitch.IsOn = _model.SkipAllDay;
            InferenceSwitch.IsEnabled = _model.InferenceSwitchEnabled;
            InferenceSwitch.IsOn = _model.InferenceIsOn;
            InferenceStatus.Text = _model.InferenceStatusText ?? string.Empty;
            InferenceStatus.Visibility = _model.InferenceStatusText is null ? Visibility.Collapsed : Visibility.Visible;
            ForgetButton.Visibility = _model.ShowForget ? Visibility.Visible : Visibility.Collapsed;
            MessageBar.Title = _model.Message ?? string.Empty;
            MessageBar.IsOpen = _model.HasMessage;
        }
        finally
        {
            _syncing = false;
        }

        if (_model.ConfirmForget && !_asking)
        {
            _ = AskToForgetAsync();
        }
    }

    // ---- the person's changes --------------------------------------------------------------------------------------------------------

    private void OnOpenAccounts(object sender, RoutedEventArgs e) => _shell.Open(SettingsPane.Accounts);

    private async void OnSkipToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
        {
            await ChangeAsync(() => _model.SetSkipAllDayAsync(SkipSwitch.IsOn));
        }
    }

    private async void OnInferenceToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing)
        {
            await ChangeAsync(() => _model.SetInferenceAsync(InferenceSwitch.IsOn));
        }
    }

    private void OnForget(object sender, RoutedEventArgs e) => _model.BeginForget();

    private async Task AskToForgetAsync()
    {
        _asking = true;
        try
        {
            var dialog = new ContentDialog
            {
                Title = _text.GetString("Calendars_ForgetTitle"),
                Content = _text.GetString("Calendars_ForgetBody"),
                PrimaryButtonText = _text.GetString("Calendars_ForgetConfirm"),
                CloseButtonText = _text.GetString("Calendars_ForgetCancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };
            Theming.ThemeService.Match(dialog, XamlRoot);
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ChangeAsync(_model.ForgetLearnedCorrectionsAsync);
            }
            else
            {
                _model.CancelForget();
            }
        }
        finally
        {
            _asking = false;
        }
    }

    private static async Task ChangeAsync(Func<Task> change)
    {
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
