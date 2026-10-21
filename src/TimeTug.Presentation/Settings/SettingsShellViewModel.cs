using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;

namespace TimeTug.Presentation.Settings;

/// <summary>A pane in the navigation list.</summary>
public sealed record SettingsPaneItem(SettingsPane Pane, string Title);

/// <summary>
/// A search result: the item, its title, the title of the pane it lives in (the second line of a result) and the one phrase a screen reader
/// reads for it. An item with an empty id is the "No results" line, which cannot be chosen.
/// </summary>
public sealed record SettingsSearchResult(SettingsSearchItem Item, string Title, string PaneTitle, string AutomationName)
{
    /// <summary>True when there is a second line to show.</summary>
    public bool HasPane => PaneTitle.Length > 0;

    /// <summary>True for a real result (not the "No results" line).</summary>
    public bool IsChoosable => Item.Id.Length > 0;
}

/// <summary>
/// The Settings window's shell: which pane is open (and which General sub-page), the search, and the control being highlighted. The Mac's
/// <c>SettingsNavigation</c> and the sidebar of <c>SettingsView</c>, ported. It knows nothing of the panes' contents.
/// <para>
/// <see cref="Open"/> switches to a pane and highlights a control for <see cref="HighlightDuration"/>; a second open restarts the highlight. A
/// control inside a General sub-page opens the page, anything else returns General to its hub so the highlighted control is visible.
/// </para>
/// </summary>
public sealed class SettingsShellViewModel : ObservableObject, IDisposable
{
    /// <summary>How long a revealed control stays highlighted.</summary>
    public static readonly TimeSpan HighlightDuration = TimeSpan.FromMilliseconds(1500);

    private readonly SettingsText _text;
    private readonly SettingsSearch _search;
    private readonly IUiSettings _ui;
    private readonly IUiDispatcher _dispatcher;
    private readonly TimeProvider _time;
    private readonly IDisposable _stateSubscription;
    private readonly object _sync = new();
    private IReadOnlyList<CalendarEntry> _calendars = [];
    private CancellationTokenSource? _highlightWait;
    private int _highlightVersion;
    private bool _disposed;

    public SettingsShellViewModel(IEngineClient engine, SettingsText text, IUiSettings ui, IUiDispatcher dispatcher, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(time);
        _text = text;
        _search = new SettingsSearch(text);
        _ui = ui;
        _dispatcher = dispatcher;
        _time = time;
        Panes = [.. Enum.GetValues<SettingsPane>().Select(p => new SettingsPaneItem(p, text.PaneTitle(p)))];
        _stateSubscription = engine.State.Subscribe(new ActionObserver<AppState>(state => _dispatcher.Post(() => OnState(state))));
    }

    /// <summary>Raised on the UI thread whenever a control is revealed, so its pane can scroll to it (even when it was already highlighted).</summary>
    public event Action<string>? HighlightRequested;

    /// <summary>The panes in the Mac's order.</summary>
    public IReadOnlyList<SettingsPaneItem> Panes { get; }

    /// <summary>The open pane. Choosing one in the navigation list sets this; the General sub-page is left as it is, as on the Mac.</summary>
    public SettingsPane SelectedPane
    {
        get;
        set => SetProperty(ref field, value);
    }

    /// <summary>The General sub-pages drilled into, outermost first (at most one today).</summary>
    public IReadOnlyList<GeneralDestination> GeneralPath
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(Destination));
                OnPropertyChanged(nameof(CanGoBack));
            }
        }
    } = [];

    /// <summary>The General sub-page showing, or null for the hub.</summary>
    public GeneralDestination? Destination => GeneralPath.Count == 0 ? null : GeneralPath[^1];

    /// <summary>True on a General sub-page: the back button shows.</summary>
    public bool CanGoBack => GeneralPath.Count > 0;

    /// <summary>What was typed in the search box.</summary>
    public string SearchText
    {
        get;
        set
        {
            if (SetProperty(ref field, value ?? string.Empty))
            {
                Recompute();
                OnPropertyChanged(nameof(IsSearching));
            }
        }
    } = string.Empty;

    /// <summary>True when the search box holds more than white space.</summary>
    public bool IsSearching => SearchText.Trim().Length > 0;

    /// <summary>True when something was typed and nothing matches.</summary>
    public bool HasNoResults => IsSearching && Results.Count == 0;

    public IReadOnlyList<SettingsSearchResult> Results
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasNoResults));
            }
        }
    } = [];

    /// <summary>The id of the control to tint now (it clears after <see cref="HighlightDuration"/>), or null.</summary>
    public string? HighlightedId
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>False when Windows asks for reduced motion: the highlight then appears and goes without fading.</summary>
    public bool AnimateHighlight => _ui.AnimationsEnabled;

    /// <summary>
    /// Switches to <paramref name="pane"/> (null: stay), drills into <paramref name="destination"/> (none: General returns to its hub) and
    /// highlights <paramref name="highlightId"/> for <see cref="HighlightDuration"/> (none: no new highlight).
    /// </summary>
    public void Open(SettingsPane? pane, string? highlightId = null, GeneralDestination? destination = null)
    {
        if (_disposed)
        {
            return;
        }

        if (pane is { } target)
        {
            SelectedPane = target;
            GeneralPath = destination is { } page ? [page] : [];
        }
        else if (destination is { } onlyPage)
        {
            SelectedPane = SettingsPane.General;
            GeneralPath = [onlyPage];
        }

        if (highlightId is not null)
        {
            Highlight(highlightId);
        }
    }

    /// <summary>Reveals a search result: its pane (and General sub-page), with the control highlighted.</summary>
    public void Reveal(SettingsSearchItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Open(item.Pane, item.Id, SettingsSearch.DestinationFor(item.Id));
    }

    /// <summary>A row on the General hub opens its sub-page.</summary>
    public void OpenDestination(GeneralDestination destination)
    {
        SelectedPane = SettingsPane.General;
        GeneralPath = [destination];
    }

    /// <summary>The back button of a General sub-page.</summary>
    public void Back()
    {
        if (GeneralPath.Count > 0)
        {
            GeneralPath = [.. GeneralPath.Take(GeneralPath.Count - 1)];
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stateSubscription.Dispose();
        lock (_sync)
        {
            _highlightWait?.Cancel();
            _highlightWait?.Dispose();
            _highlightWait = null;
        }
    }

    private void Highlight(string id)
    {
        int version;
        CancellationToken token;
        lock (_sync)
        {
            _highlightWait?.Cancel();
            _highlightWait?.Dispose();
            _highlightWait = new CancellationTokenSource();
            token = _highlightWait.Token;
            version = ++_highlightVersion;
        }

        HighlightedId = id;
        HighlightRequested?.Invoke(id);
        _ = ClearAfterAsync(version, token);
    }

    /// <summary>Waits out the highlight on the injected clock, then clears it on the UI thread (unless a newer highlight replaced it).</summary>
    private async Task ClearAfterAsync(int version, CancellationToken token)
    {
        try
        {
            await Task.Delay(HighlightDuration, _time, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _dispatcher.Post(() => ClearHighlight(version));
    }

    private void ClearHighlight(int version)
    {
        lock (_sync)
        {
            if (version != _highlightVersion)
            {
                return;
            }
        }

        HighlightedId = null;
    }

    private void OnState(AppState state)
    {
        if (_disposed)
        {
            return;
        }

        _calendars = [.. state.CalendarGroups.SelectMany(g => g.Calendars), .. state.SystemCalendars];
        Recompute();
    }

    private void Recompute()
    {
        Results = IsSearching
            ? [.. _search.Results(SearchText, _calendars).Select(i => new SettingsSearchResult(i, i.Title, _text.PaneTitle(i.Pane), string.Format(CultureInfo.CurrentCulture, _text.Get("Settings_SearchResultAutomation"), i.Title, _text.PaneTitle(i.Pane))))]
            : [];
    }
}
