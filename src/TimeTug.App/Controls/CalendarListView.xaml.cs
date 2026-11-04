using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TimeTug.App.Settings;
using TimeTug.App.Theming;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.App.Controls;

/// <summary>
/// The one list of calendars, shared by Settings > Calendars and the first-run window's "You're connected" step: one card per calendar source (a line under
/// its header when the account needs the person, an "All" switch per account group, a visibility checkbox in the calendar's colour and a Tug switch per calendar),
/// and, where the host asks, the subscribed and system calendars in a disclosure. It shows the view model's lists and passes the person's changes to it; each
/// change is one engine settings patch, saved at once. When only a value changed (a switch moved) the controls are updated where they stand, so keyboard focus
/// stays; when the set of calendars changed the cards are built again. It has no scroll bar of its own: the host scrolls.
/// </summary>
public sealed partial class CalendarListView : UserControl
{
    private const double SwitchColumn = 64;

    private readonly Dictionary<string, RowControls> _rows = [];
    private readonly List<(CalendarGroupView Group, ToggleSwitch Switch)> _groupSwitches = [];
    private readonly Dictionary<string, string> _problems = [];
    private CalendarsViewModel? _model;
    private ILocalizer? _text;
    private IUiSettings? _ui;
    private bool _includeSystem;
    private string _structure = string.Empty;
    private Expander? _disclosure;
    private bool _syncing;

    public CalendarListView()
    {
        InitializeComponent();
    }

    /// <summary>Raised after the cards were built again (the Settings pane tints the control a search result revealed).</summary>
    internal event EventHandler? Rebuilt;

    /// <summary>
    /// Connects the list to its model. Call once, before the control is shown.
    /// </summary>
    /// <param name="model">The calendars' view model: the lists to show and the changes to make.</param>
    /// <param name="text">The app's strings.</param>
    /// <param name="ui">Windows' contrast theme, which replaces the calendars' colours; null means none (the control then does not follow a change of it).</param>
    /// <param name="includeSystem">True in Settings: the subscribed and system calendars get their disclosure. False in the first-run window, which lists the everyday calendars only.</param>
    internal void Attach(CalendarsViewModel model, ILocalizer text, IUiSettings? ui, bool includeSystem)
    {
        _model = model;
        _text = text;
        _ui = ui;
        _includeSystem = includeSystem;
        Loaded += (_, _) =>
        {
            model.PropertyChanged += OnModelChanged;
            if (_ui is not null)
            {
                _ui.Changed += OnSystemChanged;
            }

            ActualThemeChanged += OnThemeChanged;
            Sync();
        };
        Unloaded += (_, _) =>
        {
            model.PropertyChanged -= OnModelChanged;
            if (_ui is not null)
            {
                _ui.Changed -= OnSystemChanged;
            }

            ActualThemeChanged -= OnThemeChanged;
        };
        Sync();
    }

    /// <summary>The disclosure of the subscribed and system calendars, when the host asked for one and there are any (for the self-test).</summary>
    internal Expander? Disclosure => _disclosure;

    /// <summary>The controls of the calendar with this key (for the self-test), or null.</summary>
    internal (CheckBox Show, ToggleSwitch Tug)? ControlsOf(string key) => _rows.TryGetValue(key, out var row) ? (row.Show, row.Tug) : null;

    /// <summary>The "All" switch of the group (for the self-test).</summary>
    internal ToggleSwitch? AllSwitchOf(string sourceId, string accountName) =>
        _groupSwitches.FirstOrDefault(g => g.Group.SourceId == sourceId && g.Group.AccountName == accountName).Switch;

    /// <summary>The line under an account's header that says it needs the person, keyed by source id (for the self-test); empty when the account has none.</summary>
    internal string ProblemOf(string sourceId) => _problems.GetValueOrDefault(sourceId, string.Empty);

    /// <summary>
    /// How the colour swatches are drawn now: in the dark or the light theme, or not at all under a contrast theme (the system colours are
    /// used and the calendar's colour is ignored). When it changes the rows are built again, because a check box takes its colours from the
    /// resources it was built with.
    /// </summary>
    internal string SwatchLook => HighContrast ? "contrast" : ActualTheme == ElementTheme.Dark ? "dark" : "light";

    private bool HighContrast => _ui?.HighContrast == true;

    private Style SecondaryCaption => (Style)Resources["SecondaryCaptionStyle"];

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void OnThemeChanged(FrameworkElement sender, object args) => Sync();

    /// <summary>Windows' contrast theme can start or end while the list is open; the swatches are drawn again (the raising thread is not necessarily this one).</summary>
    private void OnSystemChanged()
    {
        _ = DispatcherQueue.TryEnqueue(Sync);
    }

    /// <summary>Brings the cards to the model's lists: built again when the set of calendars changed, otherwise only the values.</summary>
    internal void Sync()
    {
        if (_model is not { } model)
        {
            return;
        }

        _syncing = true;
        try
        {
            Visibility = model.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
            var structure = StructureOf(model);
            if (structure != _structure)
            {
                _structure = structure;
                BuildList(model);
            }
            else
            {
                UpdateValues(model);
            }

            if (_disclosure is not null && _disclosure.IsExpanded != model.SystemExpanded)
            {
                _disclosure.IsExpanded = model.SystemExpanded;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>What the cards are made of, apart from the switch values: when it changes the cards are built again.</summary>
    private string StructureOf(CalendarsViewModel model) =>
        string.Join('\n', model.Sections.Select(s => $"{s.SourceId}|{s.Title}|{s.SubtitleText}|{s.Problem}|" + string.Join(';', s.Groups.Select(g => g.Label + "|" + string.Join(',', g.Rows.Select(r => r.Key + ":" + r.Title + ":" + r.ColorHex))))))
        + "\n--\n" + (_includeSystem ? string.Join(',', model.SystemRows.Select(r => r.Key + ":" + r.Title + ":" + r.ColorHex)) : string.Empty)
        + "\n--\n" + SwatchLook;

    private void UpdateValues(CalendarsViewModel model)
    {
        foreach (var row in model.Sections.SelectMany(s => s.Groups).SelectMany(g => g.Rows).Concat(_includeSystem ? model.SystemRows : []))
        {
            if (_rows.TryGetValue(row.Key, out var controls))
            {
                controls.Apply(row);
            }
        }

        foreach (var (group, toggle) in _groupSwitches)
        {
            var current = model.Sections.SelectMany(s => s.Groups).FirstOrDefault(g => g.SourceId == group.SourceId && g.AccountName == group.AccountName);
            if (current is not null)
            {
                toggle.IsOn = current.AllTug;
            }
        }
    }

    // ---- building the cards ------------------------------------------------------------------------------------------------------

    private void BuildList(CalendarsViewModel model)
    {
        ListPanel.Children.Clear();
        _rows.Clear();
        _groupSwitches.Clear();
        _problems.Clear();
        _disclosure = null;
        if (model.IsEmpty)
        {
            return;
        }

        var column = new Grid { Margin = new Thickness(4, 0, 4, 6) };
        column.ColumnDefinitions.Add(new ColumnDefinition());
        column.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new TextBlock { Text = model.ColumnTugCalendars, FontWeight = Microsoft.UI.Text.FontWeights.Medium, Style = SecondaryCaption };
        var right = new TextBlock { Text = model.ColumnTug, Style = SecondaryCaption, Width = SwitchColumn, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(right, 1);
        column.Children.Add(left);
        column.Children.Add(right);
        AutomationProperties.SetAccessibilityView(column, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        ListPanel.Children.Add(column);

        foreach (var section in model.Sections)
        {
            ListPanel.Children.Add(SectionHeader(section));
            if (section.Problem is { Length: > 0 } problem)
            {
                _problems[section.SourceId] = problem;
                ListPanel.Children.Add(ProblemLine(problem));
            }

            if (section.Groups.Count == 0)
            {
                continue;
            }

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

        if (_includeSystem && model.SystemRows.Count > 0)
        {
            ListPanel.Children.Add(BuildDisclosure(model));
        }

        Rebuilt?.Invoke(this, EventArgs.Empty);
    }

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

    /// <summary>The words under an account's header when the account needs the person ("Sign in again"); read right after the header.</summary>
    private TextBlock ProblemLine(string problem) => new()
    {
        Text = problem,
        Style = (Style)Resources["CalendarProblemStyle"],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(4, -2, 4, 6),
    };

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
        var all = new TextBlock { Text = _text!.GetString("Calendars_All"), Style = caption, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(all, 1);
        grid.Children.Add(all);
        var toggle = new ToggleSwitch { MinWidth = 0, OnContent = string.Empty, OffContent = string.Empty, IsOn = group.AllTug, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        AutomationProperties.SetName(toggle, group.AllAutomationName);
        toggle.Toggled += async (_, _) =>
        {
            if (!_syncing)
            {
                await ChangeAsync(() => _model!.SetGroupTugAsync(group.SourceId, group.AccountName, toggle.IsOn));
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

    private Border BuildDisclosure(CalendarsViewModel model)
    {
        var card = Card();
        var stack = (StackPanel)card.Child;
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock { Text = model.SystemTitle, VerticalAlignment = VerticalAlignment.Center });
        var count = new TextBlock { Text = model.SystemCount.ToString(System.Globalization.CultureInfo.CurrentCulture), Style = (Style)Resources["SecondaryTextStyle"], VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        Grid.SetColumn(count, 1);
        header.Children.Add(count);
        var rows = new StackPanel();
        foreach (var row in model.SystemRows)
        {
            rows.Children.Add(Divider());
            rows.Children.Add(CalendarRow(row));
        }

        var expander = new Expander
        {
            Header = header,
            Content = rows,
            IsExpanded = model.SystemExpanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Colors.Transparent),
        };
        AutomationProperties.SetName(expander, model.SystemTitle + ", " + model.SystemCount.ToString(System.Globalization.CultureInfo.CurrentCulture));
        expander.Expanding += (_, _) => model.SystemExpanded = true;
        expander.Collapsed += (_, _) => model.SystemExpanded = false;
        _disclosure = expander;
        stack.Children.Add(expander);
        card.Margin = new Thickness(0, 16, 0, 0);
        return card;
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
        private readonly CalendarListView _list;

        public RowControls(CalendarRowView row, CalendarListView list)
        {
            _list = list;
            Key = row.Key;
            Show = new CheckBox { MinWidth = 0, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
            DrawInCalendarColour(row.ColorHex);

            async void ShowChanged(object sender, RoutedEventArgs e)
            {
                if (!_list._syncing)
                {
                    await ChangeAsync(() => _list._model!.SetCalendarShownAsync(Key, Show.IsChecked == true));
                }
            }

            Show.Checked += ShowChanged;
            Show.Unchecked += ShowChanged;
            Title = new TextBlock { Text = row.Title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Tug = new ToggleSwitch { MinWidth = 0, OnContent = string.Empty, OffContent = string.Empty, HorizontalAlignment = HorizontalAlignment.Center };
            Tug.Toggled += async (_, _) =>
            {
                if (!_list._syncing)
                {
                    await ChangeAsync(() => _list._model!.SetCalendarTugAsync(Key, Tug.IsOn));
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
            if (_list.HighContrast)
            {
                return;
            }

            var swatch = SwatchColors.For(hex, _list.ActualTheme == ElementTheme.Dark);
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
