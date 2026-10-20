using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Flyout;

/// <summary>How the flyout's background is drawn.</summary>
public enum FlyoutBackdrop
{
    /// <summary>Acrylic: a live blur of what is behind the window.</summary>
    Material,

    /// <summary>A flat fill: transparency effects are off or a contrast theme is on.</summary>
    Solid,
}

/// <summary>What the window should look like, decided from the Windows appearance settings.</summary>
/// <param name="Backdrop">Material, or solid when transparency is reduced or a contrast theme is on.</param>
/// <param name="Animate">False when Windows has animations off: the window then appears without its opening animation.</param>
/// <param name="HighContrast">A contrast theme is on: the window uses the theme's own colours.</param>
public sealed record FlyoutLook(FlyoutBackdrop Backdrop, bool Animate, bool HighContrast);

/// <summary>The flyout's window, as the host sees it. The WinUI window implements it; tests use a fake.</summary>
public interface IFlyoutSurface
{
    /// <summary>The window lost the active state (the person clicked elsewhere).</summary>
    event Action? Deactivated;

    /// <summary>Esc was pressed in the window.</summary>
    event Action? EscapePressed;

    /// <summary>The window closed, whoever closed it.</summary>
    event Action? Closed;

    /// <summary>What is shown changed, so the wanted height may be different.</summary>
    event Action? ContentChanged;

    /// <summary>The size the content wants at 360 DIP wide (its height is capped by the placement).</summary>
    SizeInDips MeasureContent();

    /// <summary>Puts the window at <paramref name="bounds"/> (physical pixels), shows it and gives it the keyboard.</summary>
    void Show(PixelRect bounds, FlyoutLook look);

    /// <summary>Moves and resizes the open window, and applies a changed look.</summary>
    void Update(PixelRect bounds, FlyoutLook look);

    void Close();
}

/// <summary>Makes a new flyout window each time the flyout opens.</summary>
public interface IFlyoutSurfaceFactory
{
    IFlyoutSurface Create();
}

/// <summary>Whether the full-screen tug is up. The flyout never opens over it and gives way when it appears.</summary>
public interface ITugPresence
{
    bool IsTugShowing { get; }

    /// <summary>Raised from any thread when the tug may have appeared or gone.</summary>
    event Action? Changed;
}

/// <summary>
/// Opens, closes and places the flyout. <see cref="Toggle"/> is what a left click on the tray icon calls. Everything here runs on the
/// UI thread; signals from other threads (the tug appearing) are posted to it.
/// <list type="bullet">
/// <item>The tug wins: while it shows, <see cref="Toggle"/> does nothing, and a tug that appears closes an open flyout. The flyout never
/// takes the keyboard from it.</item>
/// <item>It closes when it loses the active state and on Esc.</item>
/// <item>Clicking the tray icon deactivates the flyout before the click reaches <see cref="Toggle"/>, so a toggle that follows a
/// deactivation closing within <see cref="ClickGrace"/> is the same click and leaves the flyout closed.</item>
/// </list>
/// </summary>
public sealed class FlyoutHost : IDisposable
{
    /// <summary>How soon after a close by deactivation a toggle still counts as the click that caused it.</summary>
    public static readonly TimeSpan ClickGrace = TimeSpan.FromMilliseconds(400);

    private const string Category = "flyout";

    private readonly IFlyoutSurfaceFactory _factory;
    private readonly ITugPresence _tug;
    private readonly IUiSettings _settings;
    private readonly ITrayAnchor _anchor;
    private readonly IMonitorService _monitors;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private readonly IDiagnostics? _diagnostics;
    private IFlyoutSurface? _surface;
    private PixelRect? _anchorRect;
    private DateTimeOffset _closedByDeactivation = DateTimeOffset.MinValue;
    private bool _disposed;

    public FlyoutHost(
        IFlyoutSurfaceFactory factory,
        ITugPresence tug,
        IUiSettings settings,
        ITrayAnchor anchor,
        IMonitorService monitors,
        IUiDispatcher ui,
        TimeProvider time,
        IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(tug);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(time);
        _factory = factory;
        _tug = tug;
        _settings = settings;
        _anchor = anchor;
        _monitors = monitors;
        _ui = ui;
        _time = time;
        _diagnostics = diagnostics;
        _tug.Changed += OnTugChanged;
        _settings.Changed += OnSettingsChanged;
    }

    /// <summary>True while the flyout window is open.</summary>
    public bool IsOpen => _surface is not null;

    /// <summary>Raised on the UI thread after the flyout opened or closed.</summary>
    public event Action? OpenChanged;

    /// <summary>Opens the flyout, or closes it when it is open. Returns true when it is open afterwards.</summary>
    public bool Toggle()
    {
        if (_disposed)
        {
            return false;
        }

        if (_surface is not null)
        {
            Close();
            return false;
        }

        if (_tug.IsTugShowing)
        {
            Note("info", "the flyout stays closed while the tug is showing");
            return false;
        }

        if (_time.GetUtcNow() - _closedByDeactivation < ClickGrace)
        {
            // The tray click that deactivated the flyout and closed it has now arrived: it was meant to close it.
            return false;
        }

        Open();
        return IsOpen;
    }

    /// <summary>Closes the flyout if it is open.</summary>
    public void Close() => Close(byDeactivation: false);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _tug.Changed -= OnTugChanged;
        _settings.Changed -= OnSettingsChanged;
        if (_ui.HasThreadAccess)
        {
            Close();
        }
        else
        {
            _ui.Post(Close);
        }
    }

    private void Open()
    {
        IFlyoutSurface surface;
        try
        {
            surface = _factory.Create();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            Note("error", $"the flyout window could not be created ({e.GetType().Name})");
            return;
        }

        _surface = surface;
        surface.Deactivated += OnDeactivated;
        surface.EscapePressed += OnEscape;
        surface.Closed += OnClosed;
        surface.ContentChanged += OnContentChanged;
        _anchorRect = _anchor.TryGetAnchor()?.Rect;
        try
        {
            surface.Show(Place(surface), Look());
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            Note("error", $"the flyout window could not be shown ({e.GetType().Name})");
            Detach(surface);
            _surface = null;
            TryClose(surface);
            return;
        }

        OpenChanged?.Invoke();
    }

    private void Close(bool byDeactivation)
    {
        if (_surface is not { } surface)
        {
            return;
        }

        if (byDeactivation)
        {
            _closedByDeactivation = _time.GetUtcNow();
        }

        Detach(surface);
        _surface = null;
        TryClose(surface);
        OpenChanged?.Invoke();
    }

    private PixelRect Place(IFlyoutSurface surface) => FlyoutPlacement.Compute(_anchorRect, surface.MeasureContent(), _monitors.Monitors);

    private FlyoutLook Look() => new(
        _settings.HighContrast || !_settings.TransparencyEffectsEnabled ? FlyoutBackdrop.Solid : FlyoutBackdrop.Material,
        _settings.AnimationsEnabled,
        _settings.HighContrast);

    private void OnDeactivated()
    {
        Note("info", "the flyout closed because it lost the active state");
        Close(byDeactivation: true);
    }

    private void OnEscape() => Close(byDeactivation: false);

    private void OnClosed()
    {
        // Closed by something other than this host (the system, the window itself).
        if (_surface is { } surface)
        {
            Detach(surface);
            _surface = null;
            OpenChanged?.Invoke();
        }
    }

    private void OnContentChanged()
    {
        if (_surface is { } surface)
        {
            surface.Update(Place(surface), Look());
        }
    }

    private void OnSettingsChanged() => _ui.Post(() =>
    {
        if (!_disposed && _surface is { } surface)
        {
            surface.Update(Place(surface), Look());
        }
    });

    private void OnTugChanged() => _ui.Post(() =>
    {
        if (!_disposed && _tug.IsTugShowing && _surface is not null)
        {
            Note("info", "the flyout closed because the tug appeared");
            Close();
        }
    });

    private void Detach(IFlyoutSurface surface)
    {
        surface.Deactivated -= OnDeactivated;
        surface.EscapePressed -= OnEscape;
        surface.Closed -= OnClosed;
        surface.ContentChanged -= OnContentChanged;
    }

    private void TryClose(IFlyoutSurface surface)
    {
        try
        {
            surface.Close();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            // The window was already gone.
            Note("info", $"the flyout window was already closed ({e.GetType().Name})");
        }
    }

    private void Note(string level, string message) => _diagnostics?.Record(level, Category, message);
}
