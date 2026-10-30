using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Theming;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;
using Windows.UI;

namespace TimeTug.App.Settings;

/// <summary>
/// The Calendars pane: one card per calendar source (an "All" switch per account group, a visibility checkbox in the calendar's colour and a
/// Tug switch per calendar), the subscribed and system calendars in a disclosure, and the options. It shows the view model's lists and
/// passes the person's changes to it. When only a value changed (a switch moved) the controls are updated where they stand, so keyboard
/// focus stays; when the set of calendars changed the cards are built again.
/// </summary>
public sealed partial class CalendarsPage : UserControl
{
    private readonly CalendarsViewModel _model;
    private readonly SettingsShellViewModel _shell;
    private readonly ILocalizer _text;
    private readonly IUiSettings _ui;
    private readonly Dictionary<string, RowControls> _rows = [];
    private readonly List<(CalendarGroupView Group, ToggleSwitch Switch)> _groupSwitches = [];
    private string _structure = string.Empty;
    private Expander? _disclosure;
    private bool _syncing;
    private bool _asking;

    public CalendarsPage(CalendarsViewModel model, SettingsShellViewModel shell, SettingsText text, ILocalizer localizer, IUiSettings ui, string title)
    {
        _model = model;
        _shell = shell;
        _text = localizer;
        _ui = ui;
        InitializeComponent();
        Heading.Text = title;
        Intro.Text = model.IntroText;
        EmptyText.Text = model.EmptyText;
        EmptyLink.Content = model.EmptyLinkText;
        AutomationProperties.SetName(EmptyLink, model.EmptyLinkText);
        OptionsHeader.Text = model.OptionsTitle;

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
            ui.Changed += OnSystemChanged;
            ActualThemeChanged += OnThemeChanged;
            Reveal();
            Sync();
        };
        Unloaded += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            shell.PropertyChanged -= OnShellChanged;
            ui.Changed -= OnSystemChanged;
            ActualThemeChanged -= OnThemeChanged;
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

    internal Expander? Disclosure => _disclosure;

    internal ScrollViewer ScrollArea => Scroller;

    /// <summary>The controls of the calendar with this key (for the self-test), or null.</summary>
    internal (CheckBox Show, ToggleSwitch Tug)? ControlsOf(string key) => _rows.TryGetValue(key, out var row) ? (row.Show, row.Tug) : null;

    /// <summary>The "All" switch of the group (for the self-test).</summary>
    internal ToggleSwitch? AllSwitchOf(string sourceId, string accountName) =>
        _groupSwitches.FirstOrDefault(g => g.Group.SourceId == sourceId && g.Group.AccountName == accountName).Switch;

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

    private void OnThemeChanged(FrameworkElement sender, object args) => Sync();

    /// <summary>Windows' contrast theme can start or end while the pane is open; the swatches are drawn again (the raising thread is not necessarily this one).</summary>
    private void OnSystemChanged()
    {
        _ = DispatcherQueue.TryEnqueue(Sync);
    }

    /// <summary>
    /// How the colour swatches are drawn now: in the dark or the light theme, or not at all under a contrast theme (the system colours are
    /// used and the calendar's colour is ignored). When it changes the rows are built again, because a check box takes its colours from the
    /// resources it was built with.
    /// </summary>
    internal string SwatchLook => _ui.HighContrast ? "contrast" : ActualTheme == ElementTheme.Dark ? "dark" : "light";

    private void Sync()
    {
        _syncing = true;
        try
        {
            var empty = _model.IsEmpty;
            EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ListPanel.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            Intro.Visibility = Visibility.Visible;
            var structure = StructureOf();
            if (structure != _structure)
            {
                _structure = structure;
                BuildList();
            }
            else
            {
                UpdateValues();
            }

            if (_disclosure is not null && _disclosure.IsExpanded != _model.SystemExpanded)
            {
                _disclosure.IsExpanded = _model.SystemExpanded;
            }

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

    /// <summary>What the cards are made of, apart from the switch values: when it changes the cards are built again.</summary>
    private string StructureOf() =>
        string.Join('\n', _model.Sections.SelectMany(s => s.Groups.Select(g => $"{s.SourceId}|{s.Title}|{s.SubtitleText}|{g.Label}|" + string.Join(',', g.Rows.Select(r => r.Key + ":" + r.Title + ":" + r.ColorHex)))))
        + "\n--\n" + string.Join(',', _model.SystemRows.Select(r => r.Key + ":" + r.Title + ":" + r.ColorHex))
        + "\n--\n" + SwatchLook;

    private void UpdateValues()
    {
        foreach (var row in _model.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Rows).Concat(_model.SystemRows))
        {
            if (_rows.TryGetValue(row.Key, out var controls))
            {
                controls.Apply(row);
            }
        }

        foreach (var (group, toggle) in _groupSwitches)
        {
            var current = _model.Sections.SelectMany(s => s.Groups).FirstOrDefault(g => g.SourceId == group.SourceId && g.AccountName == group.AccountName);
            if (current is not null)
            {
                toggle.IsOn = current.AllTug;
            }
        }
    }

    // ---- building the cards ------------------------------------------------------------------------------------------------------

    private void BuildList()
    {
        ListPanel.Children.Clear();
        _rows.Clear();
        _groupSwitches.Clear();
        _disclosure = null;
        if (_model.IsEmpty)
        {
            return;
        }

        var column = new Grid { Margin = new Thickness(4, 0, 4, 6) };
        column.ColumnDefinitions.Add(new ColumnDefinition());
        column.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TextBlock { Text = _model.ColumnTugCalendars, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Style = SecondaryCaption };
        var right = new TextBlock { Text = _model.ColumnTug, Style = SecondaryCaption, Width = SwitchColumn, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(right, 1);
        column.Children.Add(left);
        column.Children.Add(right);
        AutomationProperties.SetAccessibilityView(column, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        ListPanel.Children.Add(column);

        foreach (var section in _model.Sections)
        {
            ListPanel.Children.Add(SectionHeader(section));
            var card = Card();
            var stack = (StackPanel)card.Child;
            foreach (var group in section.Groups)
            {
                stack.Children.Add(GroupRow(group));
                foreach (var row in group.Rows)
                {
                    stack.Children.Add(Divider());
                    stack.Children.Add(CalendarRow(row));
                }
            }

            ListPanel.Children.Add(card);
        }

        if (_model.SystemRows.Count > 0)
        {
            ListPanel.Children.Add(BuildDisclosure());
        }

        SettingsHighlight.Apply(this, _shell.HighlightedId, _shell.AnimateHighlight);
    }

    private const double SwitchColumn = 64;

    private Style SecondaryCaption => (Style)Resources["SecondaryCaptionStyle"];

    private Border Card() => new()
    {
        Style = (Style)Resources["CalendarCardStyle"],
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Child = new StackPanel(),
    };

    private Border Divider() => new()
    {
        Height = 1,
        Style = (Style)Resources["CalendarDividerStyle"],
        IsHitTestVisible = false,
    };

    private StackPanel SectionHeader(CalendarSectionView section)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 16, 4, 6) };
        panel.Children.Add(new FontIcon { Glyph = GlyphOf(section), FontSize = 16 });
        panel.Children.Add(new TextBlock { Text = section.Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = section.SubtitleText,
            Style = (Style)Resources["SecondaryTextStyle"],
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        AutomationProperties.SetHeadingLevel(panel, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
        AutomationProperties.SetName(panel, section.Title + " " + section.SubtitleText);
        return panel;
    }

    /// <summary>The Mac's section icons: the calendar for the system source, the provider's for an account, a question mark for a source that is gone.</summary>
    private static string GlyphOf(CalendarSectionView section) => section.Icon switch
    {
        CalendarSectionIcon.Calendar => ProviderGlyphs.Calendar,
        CalendarSectionIcon.Unknown => ProviderGlyphs.Unknown,
        _ => ProviderGlyphs.Of(section.KindId),
    };

    private Grid GroupRow(CalendarGroupView group)
    {
        var grid = new Grid { Padding = new Thickness(12, 4, 8, 4), Style = (Style)Resources["CalendarGroupRowStyle"] };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SwitchColumn) });
        var caption = SecondaryCaption;
        grid.Children.Add(new TextBlock { Text = group.Label, Style = caption, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        var all = new TextBlock { Text = _text.GetString("Calendars_All"), Style = caption, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(all, 1);
        grid.Children.Add(all);
        var toggle = new ToggleSwitch { MinWidth = 0, OnContent = string.Empty, OffContent = string.Empty, IsOn = group.AllTug, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(toggle, group.AllAutomationName);
        toggle.Toggled += async (_, _) =>
        {
            if (!_syncing)
            {
                await ChangeAsync(() => _model.SetGroupTugAsync(group.SourceId, group.AccountName, toggle.IsOn));
            }
        };
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);
        _groupSwitches.Add((group, toggle));
        return grid;
    }

    private Grid CalendarRow(CalendarRowView row)
    {
        var host = new Grid();
        var grid = new Grid { Padding = new Thickness(12, 4, 8, 4), ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SwitchColumn) });
        var controls = new RowControls(row, this);
        grid.Children.Add(controls.Show);
        Grid.SetColumn(controls.Title, 1);
        grid.Children.Add(controls.Title);
        Grid.SetColumn(controls.Tug, 2);
        grid.Children.Add(controls.Tug);
        host.Children.Add(grid);
        SettingsHighlight.SetId(host, row.HighlightId);
        _rows[row.Key] = controls;
        return host;
    }

    private Border BuildDisclosure()
    {
        var card = Card();
        var stack = (StackPanel)card.Child;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = _model.SystemTitle, VerticalAlignment = VerticalAlignment.Center });
        var count = new TextBlock { Text = _model.SystemCount.ToString(System.Globalization.CultureInfo.CurrentCulture), Style = (Style)Resources["SecondaryTextStyle"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        var rows = new StackPanel();
        foreach (var row in _model.SystemRows)
        {
            rows.Children.Add(Divider());
            rows.Children.Add(CalendarRow(row));
        }

        var expander = new Expander
        {
            Header = header,
            Content = rows,
            IsExpanded = _model.SystemExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        AutomationProperties.SetName(expander, _model.SystemTitle + ", " + _model.SystemCount.ToString(System.Globalization.CultureInfo.CurrentCulture));
        expander.Expanding += (_, _) => _model.SystemExpanded = true;
        expander.Collapsed += (_, _) => _model.SystemExpanded = false;
        _disclosure = expander;
        stack.Children.Add(expander);
        card.Margin = new Thickness(0, 16, 0, 0);
        return card;
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

    /// <summary>One calendar's controls: the visibility checkbox in the calendar's colour, its name and the Tug switch.</summary>
    private sealed class RowControls
    {
        private readonly CalendarsPage _page;

        public RowControls(CalendarRowView row, CalendarsPage page)
        {
            _page = page;
            Key = row.Key;
            Show = new CheckBox { MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            DrawInCalendarColour(row.ColorHex);

            async void ShowChanged(object sender, RoutedEventArgs e)
            {
                if (!_page._syncing)
                {
                    await ChangeAsync(() => _page._model.SetCalendarShownAsync(Key, Show.IsChecked == true));
                }
            }

            Show.Checked += ShowChanged;
            Show.Unchecked += ShowChanged;
            Title = new TextBlock { Text = row.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Tug = new ToggleSwitch { MinWidth = 0, OnContent = string.Empty, OffContent = string.Empty, HorizontalAlignment = HorizontalAlignment.Center };
            Tug.Toggled += async (_, _) =>
            {
                if (!_page._syncing)
                {
                    await ChangeAsync(() => _page._model.SetCalendarTugAsync(Key, Tug.IsOn));
                }
            };
            Apply(row);
        }

        public string Key { get; }

        public CheckBox Show { get; }

        public TextBlock Title { get; }

        public ToggleSwitch Tug { get; }

        public void Apply(CalendarRowView row)
        {
            Show.IsChecked = row.IsShown;
            AutomationProperties.SetName(Show, row.ShowAutomationName);
            ToolTipService.SetToolTip(Show, row.ShowToolTip);
            Tug.IsOn = row.Tugs;
            Tug.IsEnabled = row.TugEnabled;
            Tug.Opacity = row.TugEnabled ? 1 : 0.6;
            AutomationProperties.SetName(Tug, row.TugAutomationName);
            ToolTipService.SetToolTip(Tug, row.TugToolTip.Length == 0 ? null : row.TugToolTip);
        }

        /// <summary>
        /// The check box is the calendar's colour: filled when shown, an outline when hidden. The mark and the edge are chosen against that colour
        /// and the card (<see cref="SwatchColors"/>): white or near-black, whichever reads at 4.5 to 1, and the calendar's own colour as the edge
        /// only where it shows against the card, the theme's foreground where it does not. Under a contrast theme nothing is set, so the
        /// system's own check box colours apply and the calendar's colour is ignored.
        /// </summary>
        private void DrawInCalendarColour(string? hex)
        {
            if (_page._ui.HighContrast)
            {
                return;
            }

            var swatch = SwatchColors.For(hex, _page.ActualTheme == ElementTheme.Dark);
            var fill = SwatchColors.Brush(swatch.Fill);
            var edge = SwatchColors.Brush(swatch.Edge);
            var mark = SwatchColors.Brush(swatch.Mark);
            foreach (var state in new[] { "", "PointerOver", "Pressed" })
            {
                Show.Resources["CheckBoxCheckBackgroundFillChecked" + state] = fill;
                Show.Resources["CheckBoxCheckBackgroundStrokeChecked" + state] = edge;
                Show.Resources["CheckBoxCheckBackgroundStrokeUnchecked" + state] = edge;
                Show.Resources["CheckBoxCheckGlyphForegroundChecked" + state] = mark;
            }
        }
    }
}
