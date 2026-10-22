using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Tray;

namespace TimeTug.App.Settings;

/// <summary>
/// The Appearance pane: the tray icon style, the flyout backdrop and the theme (the Mac's Menu Bar Text, Popup Cards and Light/Dark/Auto), each
/// a row of selectable tiles with a live preview and a label. It shows the view model's values and passes the person's choice to it; the
/// choice is saved at once and applied to the open windows by <see cref="Theming.ThemeService"/>, the tray and the flyout host, which listen to the
/// store. The previews use the theme's own brushes, so they follow the theme the moment it is applied.
/// </summary>
public sealed partial class AppearancePage : UserControl
{
    private readonly AppearanceViewModel _model;
    private readonly List<(RadioButton Tile, TrayPreview Preview)> _trayTiles = [];
    private readonly List<RadioButton> _backdropTiles = [];
    private readonly List<RadioButton> _themeTiles = [];
    private bool _syncing;

    public AppearancePage(AppearanceViewModel model, SettingsText text, ILocalizer localizer, string title)
    {
        _model = model;
        InitializeComponent();
        Heading.Text = title;
        TrayHeader.Text = text.ControlTitle(AppearanceViewModel.Ids.TrayStyle);
        BackdropHeader.Text = text.ControlTitle(AppearanceViewModel.Ids.Backdrop);
        ThemeHeader.Text = text.ControlTitle(AppearanceViewModel.Ids.Theme);
        AutomationProperties.SetName(TrayTiles, TrayHeader.Text);
        AutomationProperties.SetName(BackdropTiles, BackdropHeader.Text);
        AutomationProperties.SetName(ThemeTiles, ThemeHeader.Text);
        TrayNote.Text = model.TrayNote;
        BackdropNote.Text = model.BackdropNote;
        ThemeNote.Text = model.ThemeNote;
        SettingsHighlight.SetId(TrayHost, AppearanceViewModel.Ids.TrayStyle);
        SettingsHighlight.SetId(BackdropHost, AppearanceViewModel.Ids.Backdrop);
        SettingsHighlight.SetId(ThemeHost, AppearanceViewModel.Ids.Theme);

        var renderer = NewRenderer();
        var clock = localizer.GetString("Appearance_Preview_Clock");
        foreach (var choice in model.TrayChoices)
        {
            var preview = new TrayPreview(choice.Value, renderer, clock);
            var tile = Tile(choice, preview, "TrayIconStyle", TrayTiles, OnTrayChecked);
            _trayTiles.Add((tile, preview));
        }

        foreach (var choice in model.BackdropChoices)
        {
            _backdropTiles.Add(Tile(choice, new BackdropPreview(choice.Value, localizer), "FlyoutBackdrop", BackdropTiles, OnBackdropChecked));
        }

        foreach (var choice in model.ThemeChoices)
        {
            _themeTiles.Add(Tile(choice, new ThemePreview(choice.Value), "Theme", ThemeTiles, OnThemeChecked));
        }

        // Listen only while the page is on screen (the window keeps the page when another pane is open).
        Loaded += (_, _) =>
        {
            model.PropertyChanged += OnModelChanged;
            Sync();
        };
        Unloaded += (_, _) => model.PropertyChanged -= OnModelChanged;
        Sync();
    }

    /// <summary>The tiles of each setting (for the self-test).</summary>
    internal IReadOnlyList<RadioButton> TrayStyleTiles => _trayTiles.ConvertAll(t => t.Tile);

    /// <summary>The picture each tray style tile shows (null when the tray picture files could not be read).</summary>
    internal IReadOnlyList<Microsoft.UI.Xaml.Media.ImageSource?> TrayPreviewPictures => _trayTiles.ConvertAll(t => t.Preview.Shown);

    internal IReadOnlyList<RadioButton> BackdropChoiceTiles => _backdropTiles;

    internal IReadOnlyList<RadioButton> ThemeChoiceTiles => _themeTiles;

    /// <summary>The note that says Windows overrides the backdrop (open only while it does).</summary>
    internal InfoBar OverrideNotice => OverrideBar;

    internal TextBlock BackdropNoteText => BackdropNote;

    internal ScrollViewer ScrollArea => Scroller;

    private static TrayIconRenderer? NewRenderer()
    {
        try
        {
            return new TrayIconRenderer(new TrayIconFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "Tray")));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private RadioButton Tile<T>(AppearanceChoice<T> choice, FrameworkElement preview, string group, Panel host, RoutedEventHandler onChecked)
    {
        var label = new TextBlock { Text = choice.Title, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var content = new StackPanel { Spacing = 6, Width = preview.Width };
        content.Children.Add(preview);
        content.Children.Add(label);
        var tile = new RadioButton { GroupName = group, Tag = choice.Value, Content = content, Style = (Style)Resources["TileRadioStyle"] };
        AutomationProperties.SetName(tile, choice.AutomationName);
        AutomationProperties.SetHelpText(tile, choice.Detail);
        tile.Checked += onChecked;
        host.Children.Add(tile);
        return tile;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Sync();

    private void OnTrayChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncing && sender is RadioButton { Tag: TrayIconStyle style })
        {
            _model.SetTrayStyle(style);
        }
    }

    private void OnBackdropChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncing && sender is RadioButton { Tag: FlyoutBackdrop backdrop })
        {
            _model.SetBackdrop(backdrop);
        }
    }

    private void OnThemeChecked(object sender, RoutedEventArgs e)
    {
        if (!_syncing && sender is RadioButton { Tag: AppTheme theme })
        {
            _model.SetTheme(theme);
        }
    }

    /// <summary>Puts every tile and note to the view model's values without raising the person's-choice handlers.</summary>
    private void Sync()
    {
        _syncing = true;
        try
        {
            foreach (var (tile, preview) in _trayTiles)
            {
                tile.IsChecked = tile.Tag is TrayIconStyle style && style == _model.TrayStyle;
                preview.Update(_model.TaskbarLight);
            }

            foreach (var tile in _backdropTiles)
            {
                tile.IsChecked = tile.Tag is FlyoutBackdrop backdrop && backdrop == _model.Backdrop;
            }

            foreach (var tile in _themeTiles)
            {
                tile.IsChecked = tile.Tag is AppTheme theme && theme == _model.Theme;
            }

            OverrideBar.Message = _model.BackdropOverrideText;
            OverrideBar.IsOpen = _model.BackdropOverridden;
        }
        finally
        {
            _syncing = false;
        }
    }
}
