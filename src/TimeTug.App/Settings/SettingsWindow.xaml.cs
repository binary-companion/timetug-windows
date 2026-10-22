using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;
using Windows.Graphics;

namespace TimeTug.App.Settings;

/// <summary>
/// The Settings window: Mica behind a navigation view with the five panes and a search box. It holds no settings logic. Which pane is open
/// and what the search finds come from <see cref="SettingsShellViewModel"/>; the pane pages show their own content. It remembers its size
/// and place between runs (kept in the presentation settings; a place on a display that is gone is discarded) and does not close on Esc.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "The save delay's token source is cancelled and disposed when the window closes (OnClosed).")]
public sealed partial class SettingsWindow : Window
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(600);

    private readonly SettingsShellViewModel _shell;
    private readonly SettingsText _text;
    private readonly ILocalizer _localizer;
    private readonly IPresentationSettingsStore _store;
    private readonly IMonitorService _monitors;
    private readonly IUiSettings _ui;
    private readonly SettingsPaneModels _models;
    private readonly List<IDisposable> _ownedModels = [];
    private readonly Dictionary<SettingsPane, FrameworkElement> _pages = [];
    private readonly Dictionary<SettingsPane, NavigationViewItem> _items = [];
    private CancellationTokenSource? _saveWait;
    private bool _syncing;
    private bool _closed;

    public SettingsWindow(
        SettingsShellViewModel shell, SettingsText text, ILocalizer localizer, IPresentationSettingsStore store, IMonitorService monitors, IUiSettings ui,
        SettingsPaneModels models)
    {
        _models = models;
        _shell = shell;
        _text = text;
        _localizer = localizer;
        _store = store;
        _monitors = monitors;
        _ui = ui;
        InitializeComponent();
        Title = localizer.GetString("Settings_WindowTitle");
        AutomationProperties.SetName(Nav, localizer.GetString("Settings_PaneNavigation"));
        AutomationProperties.SetName(Search, localizer.GetString("Settings_SearchAutomation"));
        Search.PlaceholderText = localizer.GetString("Settings_SearchPlaceholder");

        foreach (var pane in shell.Panes)
        {
            var item = new NavigationViewItem
            {
                Content = pane.Title,
                Tag = pane.Pane,
                Icon = new FontIcon { Glyph = GlyphOf(pane.Pane) },
            };
            AutomationProperties.SetName(item, pane.Title);
            _items[pane.Pane] = item;
            Nav.MenuItems.Add(item);
        }

        Place();
        ShowPane(shell.SelectedPane);
        shell.PropertyChanged += OnShellChanged;
        shell.HighlightRequested += OnHighlightRequested;
        AppWindow.Changed += OnAppWindowChanged;
        Activated += OnActivated;
        Closed += OnClosed;
    }

    /// <summary>The window closed, whoever closed it.</summary>
    public event Action? WindowClosed;

    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    public SettingsShellViewModel Shell => _shell;

    /// <summary>The navigation view (for the self-test).</summary>
    public NavigationView Navigation => Nav;

    /// <summary>The search box (for the self-test).</summary>
    public AutoSuggestBox SearchBox => Search;

    /// <summary>The open pane's page (for the self-test).</summary>
    public FrameworkElement? CurrentPage => PaneHost.Content as FrameworkElement;

    public FrameworkElement RootElement => Root;

    /// <summary>Shows the window and brings it to the front; a minimized window is restored first.</summary>
    public void ShowAndFocus()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
    }

    /// <summary>Runs the search for this text as if it was typed (for the self-test).</summary>
    public void TypeInSearch(string text)
    {
        Search.Text = text;
        UpdateSuggestions(text);
    }

    private static string GlyphOf(SettingsPane pane) => pane switch
    {
        SettingsPane.General => "",
        SettingsPane.Appearance => "",
        SettingsPane.Accounts => "",
        SettingsPane.Calendars => "",
        SettingsPane.TugRules => "",
        _ => "",
    };

    // ---- size and place ---------------------------------------------------------------------------------------------

    private void Place()
    {
        var geometry = SettingsWindowPlacement.Compute(_store.Load().SettingsWindow, _monitors.Monitors);
        AppWindow.MoveAndResize(new RectInt32(geometry.X, geometry.Y, geometry.Width, geometry.Height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            var scale = ScaleAt(geometry);
            presenter.PreferredMinimumWidth = (int)Math.Round(SettingsWindowPlacement.MinimumSize.Width * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(SettingsWindowPlacement.MinimumSize.Height * scale);
        }
    }

    /// <summary>The scale of the display the window was placed on (1.0 at 96 DPI).</summary>
    private double ScaleAt(WindowGeometry geometry)
    {
        var centre = (X: geometry.X + (geometry.Width / 2.0), Y: geometry.Y + (geometry.Height / 2.0));
        var monitor = _monitors.Monitors.FirstOrDefault(m => m.Bounds.Contains(centre.X, centre.Y)) ?? _monitors.Monitors.FirstOrDefault(m => m.IsPrimary);
        return monitor?.Scale ?? 1.0;
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_closed || !(args.DidSizeChange || args.DidPositionChange))
        {
            return;
        }

        // A drag raises this over and over: keep the last place a moment after the person stops.
        _saveWait?.Cancel();
        _saveWait?.Dispose();
        _saveWait = new CancellationTokenSource();
        _ = SaveSoonAsync(_saveWait.Token);
    }

    private async Task SaveSoonAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SaveDelay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_closed)
            {
                SaveGeometry();
            }
        });
    }

    /// <summary>Keeps the window's place, unless it is maximized or minimized (the place to come back to is the restored one).</summary>
    private void SaveGeometry()
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            return;
        }

        var geometry = new WindowGeometry(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        var settings = _store.Load();
        if (settings.SettingsWindow != geometry)
        {
            _store.Save(settings with { SettingsWindow = geometry });
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs args)
    {
#if SELFTEST
        if (IgnoreSystemDeactivation)
        {
            return;
        }
#endif
        HandleActivation(args.WindowActivationState == WindowActivationState.Deactivated);
    }

    private void HandleActivation(bool deactivated)
    {
        if (_closed || PaneHost.Content is not GeneralPage general)
        {
            return;
        }

        if (deactivated)
        {
            // The keyboard went elsewhere: a shortcut being recorded is not recorded any more, and the registered one works again.
            general.CancelRecording();
        }
        else
        {
            // Back from Windows Settings (Startup apps) or Task Manager: launch at login may have been changed there.
            general.Refresh();
        }
    }

#if SELFTEST
    /// <summary>Self-test only: real losses of the active state are ignored (on the VM a helper takes the keyboard whenever a script runs a command); <see cref="LoseActivation"/> stands for one.</summary>
    public bool IgnoreSystemDeactivation { get; set; }

    /// <summary>Self-test only: does what losing the active state does.</summary>
    public void LoseActivation() => HandleActivation(deactivated: true);
#endif

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        foreach (var model in _ownedModels)
        {
            model.Dispose();
        }

        _ownedModels.Clear();
        _saveWait?.Cancel();
        _saveWait?.Dispose();
        _saveWait = null;
        SaveGeometry();
        _shell.PropertyChanged -= OnShellChanged;
        _shell.HighlightRequested -= OnHighlightRequested;
        AppWindow.Changed -= OnAppWindowChanged;
        WindowClosed?.Invoke();
    }

    // ---- panes -------------------------------------------------------------------------------------------------------

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_syncing && args.SelectedItem is NavigationViewItem { Tag: SettingsPane pane })
        {
            _shell.SelectedPane = pane;
        }
    }

    private void OnShellChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SettingsShellViewModel.SelectedPane):
                ShowPane(_shell.SelectedPane);
                break;
            case nameof(SettingsShellViewModel.HighlightedId):
                ApplyHighlight();
                break;
        }
    }

    private void ShowPane(SettingsPane pane)
    {
        _syncing = true;
        try
        {
            Nav.SelectedItem = _items[pane];
        }
        finally
        {
            _syncing = false;
        }

        if (!_pages.TryGetValue(pane, out var page))
        {
            _pages[pane] = page = CreatePage(pane);
        }

        PaneHost.Content = page;
        // The new page has not been laid out yet; tint its control once it has.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, ApplyHighlight);
    }

    private FrameworkElement CreatePage(SettingsPane pane) => pane switch
    {
        SettingsPane.General => NewGeneralPage(),
        SettingsPane.Appearance => new AppearancePage(_text.PaneTitle(pane)),
        SettingsPane.Accounts => NewAccountsPage(),
        SettingsPane.Calendars => NewCalendarsPage(),
        _ => NewTugRulesPage(),
    };

    private GeneralPage NewGeneralPage()
    {
        var model = _models.CreateGeneral();
        _ownedModels.Add(model);
        return new GeneralPage(_shell, _text, _localizer, model, () => ApplyHighlight());
    }

    private AccountsPage NewAccountsPage()
    {
        var accounts = _models.CreateAccounts();
        var diagnostics = _models.CreateDiagnostics();
        _ownedModels.Add(accounts);
        _ownedModels.Add(diagnostics);
        return new AccountsPage(accounts, diagnostics, _shell, _text, _localizer, _text.PaneTitle(SettingsPane.Accounts));
    }

    private CalendarsPage NewCalendarsPage()
    {
        var model = _models.CreateCalendars();
        _ownedModels.Add(model);
        return new CalendarsPage(model, _shell, _text, _localizer, _text.PaneTitle(SettingsPane.Calendars));
    }

    private TugRulesPage NewTugRulesPage()
    {
        var model = _models.CreateTugRules();
        _ownedModels.Add(model);
        return new TugRulesPage(model, _text, _localizer, _text.PaneTitle(SettingsPane.TugRules));
    }

    private void ApplyHighlight()
    {
        if (CurrentPage is { } page)
        {
            SettingsHighlight.Apply(page, _shell.HighlightedId, _shell.AnimateHighlight);
        }
    }

    private void OnHighlightRequested(string id)
    {
        // After the page for the revealed pane exists and has been laid out.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (!_closed && CurrentPage is { } page)
            {
                SettingsHighlight.BringIntoView(page, id, _shell.AnimateHighlight);
            }
        });
    }

    // ---- search ------------------------------------------------------------------------------------------------------

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            UpdateSuggestions(sender.Text);
        }
    }

    private void UpdateSuggestions(string text)
    {
        _shell.SearchText = text;
        if (!_shell.IsSearching)
        {
            Search.ItemsSource = null;
            return;
        }

        Search.ItemsSource = _shell.Results.Count > 0
            ? _shell.Results
            : new[] { new SettingsSearchResult(new SettingsSearchItem(string.Empty, _text.NoResults, [], SettingsPane.General), _text.NoResults, string.Empty, _text.NoResults) };
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SettingsSearchResult result)
        {
            Choose(result);
        }
    }

    /// <summary>Reveals a result (the "No results" line cannot be chosen).</summary>
    public void Choose(SettingsSearchResult result)
    {
        if (result.IsChoosable)
        {
            _shell.Reveal(result.Item);
        }
    }

    private void OnQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        // Enter on the typed text goes to the first match, as it does for a chosen suggestion.
        if (args.ChosenSuggestion is null && _shell.Results.Count > 0)
        {
            _shell.Reveal(_shell.Results[0].Item);
        }
    }
}
