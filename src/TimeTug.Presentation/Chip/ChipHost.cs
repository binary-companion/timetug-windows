using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Engine;
using TimeTug.Presentation.Flyout;
using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Chip;

/// <summary>
/// Shows, hides, places and closes the countdown chip. It talks only to interfaces: <see cref="ChipViewModel"/> says what the chip shows, <see cref="IChipWindow"/>
/// is the window, the monitors and the tray anchor say where there is room. Everything here runs on the UI thread; signals from other threads (the view model,
/// the tug, a monitor change, the poll) are posted to it.
/// <list type="bullet">
/// <item>The chip exists while the plan is visible: when the plan turns hidden (mode Off, no meeting in range, hidden until the next one) the window is closed.</item>
/// <item>The tug wins: while it shows, the chip is hidden, and it comes back when the tug is gone.</item>
/// <item>It never covers an exclusive full-screen app: <c>SHQueryUserNotificationState</c> has no change event, so while the plan is visible the host looks at
/// <see cref="INotificationState"/> on every change of the plan and on a 2 second poll, and hides the chip on the transition (and shows it again on the way back).</item>
/// <item>It is placed by <see cref="ChipPlacement"/> and placed again whenever the monitors, a work area, a DPI, the tray anchor or the remembered
/// positions change, so it is never off-screen; a person's drag is remembered per monitor, a double click forgets it.</item>
/// <item>It never takes focus: the window class makes sure of that; the host never asks for activation.</item>
/// </list>
/// </summary>
public sealed class ChipHost : IDisposable
{
    /// <summary>How often a full-screen app that started or ended is looked for while the chip has something to show.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private const string Category = "chip";

    private readonly ChipViewModel _model;
    private readonly IChipWindowFactory _factory;
    private readonly IChipActions _actions;
    private readonly ITugPresence _tug;
    private readonly INotificationState _notifications;
    private readonly IPollTicker _poll;
    private readonly IMonitorService _monitors;
    private readonly ITrayAnchor _anchor;
    private readonly IPresentationSettingsStore _settings;
    private readonly IUiDispatcher _ui;
    private readonly IDiagnostics? _diagnostics;
    private IChipWindow? _window;
    private IDisposable? _pollSubscription;
    private ChipPlan? _contentPlan;
    private ChipPlan? _announcedPlan;
    private PixelRect? _bounds;
    private bool _shown;
    private bool _blockedNoted;
    private bool _disposed;

    public ChipHost(
        ChipViewModel model,
        IChipWindowFactory factory,
        IChipActions actions,
        ITugPresence tug,
        INotificationState notifications,
        IPollTicker poll,
        IMonitorService monitors,
        ITrayAnchor anchor,
        IPresentationSettingsStore settings,
        IUiDispatcher ui,
        IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(tug);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(ui);
        _model = model;
        _factory = factory;
        _actions = actions;
        _tug = tug;
        _notifications = notifications;
        _poll = poll;
        _monitors = monitors;
        _anchor = anchor;
        _settings = settings;
        _ui = ui;
        _diagnostics = diagnostics;
        model.Changed += OnSignal;
        tug.Changed += OnSignal;
        monitors.Changed += OnSignal;
        anchor.Changed += OnSignal;
        settings.Changed += OnSignal;
    }

    /// <summary>True while a chip window exists (shown, or hidden for the tug or a full-screen app).</summary>
    public bool HasWindow => _window is not null;

    /// <summary>True while the chip is on screen.</summary>
    public bool IsShown => _shown;

    /// <summary>Looks at everything once and brings the window to match. The shell calls it after the first state, the settings and the monitors are known.</summary>
    public void Start() => OnUi(Evaluate);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model.Changed -= OnSignal;
        _tug.Changed -= OnSignal;
        _monitors.Changed -= OnSignal;
        _anchor.Changed -= OnSignal;
        _settings.Changed -= OnSignal;
        OnUi(() =>
        {
            StopPolling();
            Teardown();
        });
    }

    private void OnSignal() => _ui.Post(Evaluate);

    private void OnUi(Action action)
    {
        if (_ui.HasThreadAccess)
        {
            action();
        }
        else
        {
            _ui.Post(action);
        }
    }

    private void Evaluate()
    {
        if (_disposed)
        {
            return;
        }

        var plan = _model.Plan;
        if (!plan.Visible)
        {
            StopPolling();
            Teardown();
            return;
        }

        StartPolling();
        if (_tug.IsTugShowing || !_notifications.CanShowFullScreen)
        {
            Conceal(_tug.IsTugShowing ? "the chip is hidden while the tug is showing" : "the chip is hidden over a full-screen app");
            return;
        }

        _blockedNoted = false;
        var window = EnsureWindow();
        if (window is null)
        {
            return;
        }

        if (!ReferenceEquals(plan, _contentPlan))
        {
            window.SetContent(plan);
            _contentPlan = plan;
        }

        var wanted = Place(window);
        if (!_shown)
        {
            window.Show(wanted);
            _shown = true;
            _bounds = wanted;
        }
        else if (!window.IsDragging && !Same(_bounds, wanted))
        {
            window.Move(wanted);
            _bounds = wanted;
        }

        if (plan.Announcement is { } announcement && !ReferenceEquals(plan, _announcedPlan))
        {
            _announcedPlan = plan;
            window.Announce(announcement);
        }
    }

    private PixelRect Place(IChipWindow window) =>
        ChipPlacement.Compute(_settings.Load().ChipPositions, _monitors.Monitors, _anchor.Peek()?.Rect, window.MeasureContent());

    private IChipWindow? EnsureWindow()
    {
        if (_window is not null)
        {
            return _window;
        }

        try
        {
            var window = _factory.Create();
            window.Clicked += OnClicked;
            window.JoinRequested += OnJoin;
            window.HideRequested += OnHide;
            window.SettingsRequested += OnSettings;
            window.Dragged += OnDragged;
            window.ResetRequested += OnReset;
            window.Closed += OnWindowClosed;
            _window = window;
            _contentPlan = null;
            return window;
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            Note("error", $"the chip window could not be created ({e.GetType().Name})");
            return null;
        }
    }

    /// <summary>Hides the window for now (the tug or a full-screen app has the screen); it is shown again by the next evaluation that finds the way clear.</summary>
    private void Conceal(string reason)
    {
        if (!_blockedNoted)
        {
            _blockedNoted = true;
            Note("info", reason);
        }

        if (_window is { } window && _shown)
        {
            window.Hide();
            _shown = false;
        }
    }

    /// <summary>Closes the window for good (nothing to show, or the app is quitting).</summary>
    private void Teardown()
    {
        _blockedNoted = false;
        if (_window is not { } window)
        {
            return;
        }

        _window = null;
        _shown = false;
        _bounds = null;
        _contentPlan = null;
        Detach(window);
        try
        {
            window.Dispose();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            // The window was already gone.
            Note("info", $"the chip window was already closed ({e.GetType().Name})");
        }
    }

    private void Detach(IChipWindow window)
    {
        window.Clicked -= OnClicked;
        window.JoinRequested -= OnJoin;
        window.HideRequested -= OnHide;
        window.SettingsRequested -= OnSettings;
        window.Dragged -= OnDragged;
        window.ResetRequested -= OnReset;
        window.Closed -= OnWindowClosed;
    }

    private void StartPolling() => _pollSubscription ??= _poll.Start(PollInterval, OnSignal);

    private void StopPolling()
    {
        _pollSubscription?.Dispose();
        _pollSubscription = null;
    }

    private void OnClicked() => _actions.ToggleFlyout();

    private void OnJoin()
    {
        if (_model.Plan is { CanJoin: true, EventId: { } id })
        {
            _ = _actions.JoinAsync(id).AsTask();
        }
    }

    private void OnHide() => _model.HideUntilNextMeeting();

    private void OnSettings() => _actions.OpenAppearanceSettings();

    /// <summary>The person dropped the chip: remember where, per monitor, as DIPs from that monitor's work area.</summary>
    private void OnDragged(PixelRect dropped)
    {
        _bounds = dropped;
        if (ChipPlacement.ToPosition(dropped, _monitors.Monitors) is not { } position)
        {
            return;
        }

        var current = _settings.Load();
        _settings.Save(current with
        {
            ChipPositions = new ValueList<ChipPosition>(
                current.ChipPositions.Where(p => !string.Equals(p.MonitorId, position.MonitorId, StringComparison.Ordinal)).Append(position).TakeLast(ChipSettings.MaxPositions)),
        });
    }

    /// <summary>Double click: forget where the chip was dragged on the monitor it is on, so it goes back to the default place.</summary>
    private void OnReset()
    {
        if (_bounds is not { } bounds || ChipPlacement.ToPosition(bounds, _monitors.Monitors) is not { } here)
        {
            return;
        }

        var current = _settings.Load();
        if (current.ChipPositions.All(p => !string.Equals(p.MonitorId, here.MonitorId, StringComparison.Ordinal)))
        {
            // Nothing was remembered here, but the chip may have been dragged without a drop being saved: still put it back.
            _bounds = null;
            Evaluate();
            return;
        }

        _bounds = null;
        _settings.Save(current with
        {
            ChipPositions = new ValueList<ChipPosition>(current.ChipPositions.Where(p => !string.Equals(p.MonitorId, here.MonitorId, StringComparison.Ordinal))),
        });
    }

    private void OnWindowClosed()
    {
        // Closed by something other than this host (the system, the window itself): the next evaluation makes a new one when it is still wanted.
        if (_window is { } window)
        {
            Detach(window);
            _window = null;
            _shown = false;
            _bounds = null;
            _contentPlan = null;
        }
    }

    /// <summary>The same place within a pixel (the saved position is rounded to hundredths of a DIP, so a drop comes back a pixel off).</summary>
    private static bool Same(PixelRect? a, PixelRect b) =>
        a is { } r && Math.Abs(r.X - b.X) <= 1 && Math.Abs(r.Y - b.Y) <= 1 && r.Width == b.Width && r.Height == b.Height;

    private void Note(string level, string message) => _diagnostics?.Record(level, Category, message);
}
