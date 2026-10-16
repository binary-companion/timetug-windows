using Microsoft.UI.Xaml;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Tug;

namespace TimeTug.App.Tug;

/// <summary>
/// Draws the controller's plans: one borderless topmost window per monitor in the plan, sized to the whole monitor (taskbar area
/// included), the controls on the primary and a mirror on each of the others. It owns no tug logic. A plan arrives on a controller
/// thread, inside the controller's lock, so this only posts it to the UI thread and applies it there; it never calls the
/// controller back from the handler.
/// <para>
/// Windows are kept when nothing about them changed, so a monitor unplugged while the tug is up closes only that window and the
/// others stay; when the primary monitor goes, the windows whose role changed are rebuilt and the controls move to the new
/// primary. When Windows' appearance settings change (contrast theme, transparency, animations) the windows are rebuilt too,
/// without announcing again.
/// </para>
/// </summary>
internal sealed class TugWindowHost : IDisposable
{
    private readonly TugController _controller;
    private readonly IUiDispatcher _ui;
    private readonly IUiSettings _settings;
    private readonly ILocalizer _text;
    private readonly TimeProvider _time;
    private readonly ISecondTicker _ticker;
    private readonly List<OpenWindow> _open = [];
    private IDisposable? _tick;
    private string? _requestId;
    private bool _started;
    private bool _disposed;

    public TugWindowHost(TugController controller, IUiDispatcher ui, IUiSettings settings, ILocalizer text, TimeProvider time, ISecondTicker ticker)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(ticker);
        _controller = controller;
        _ui = ui;
        _settings = settings;
        _text = text;
        _time = time;
        _ticker = ticker;
    }

    /// <summary>The primary window while the tug is up (for the self-test and the composition root).</summary>
    public PrimaryTugWindow? Primary => _open.Select(o => o.Window).OfType<PrimaryTugWindow>().FirstOrDefault();

    public IReadOnlyList<MirrorTugWindow> Mirrors => _open.Select(o => o.Window).OfType<MirrorTugWindow>().ToList();

    /// <summary>The number of tug windows open now.</summary>
    public int WindowCount => _open.Count;

    /// <summary>Starts listening to the controller and draws its current plan.</summary>
    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _controller.PlanChanged += OnPlanChanged;
        _settings.Changed += OnSettingsChanged;
        Post(() => Apply(_controller.Plan, announce: true));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller.PlanChanged -= OnPlanChanged;
        _settings.Changed -= OnSettingsChanged;
        if (_ui.HasThreadAccess)
        {
            CloseAll();
        }
        else
        {
            _ui.Post(CloseAll);
        }
    }

    // ---- from the controller and the settings -------------------------------------------------------------------

    private void OnPlanChanged(TugPlan plan) => Post(() => Apply(plan, announce: true));

    private void OnSettingsChanged() => Post(Restyle);

    private void Post(Action action) => _ui.Post(() =>
    {
        if (!_disposed)
        {
            action();
        }
    });

    // ---- on the UI thread ---------------------------------------------------------------------------------------

    private void Apply(TugPlan plan, bool announce)
    {
        var changes = TugWindowDiff.Compute(_requestId, _open.Select(o => o.Plan).ToList(), plan);
        var gone = changes.Close.Select(g => _open.First(o => o.Plan == g)).ToList();
        if (plan.Mode != TugMode.FullScreen || plan.Request is null || plan.Monitors.Count == 0)
        {
            foreach (var window in gone)
            {
                Close(window);
            }

            _requestId = null;
            StopTicking();
            return;
        }

        // They are about to be replaced: whatever activation changes the replacement causes are not the person leaving.
        foreach (var window in gone)
        {
            (window.Window as PrimaryTugWindow)?.Retire();
        }

        var isNewRequest = _requestId != plan.Request.EventId;
        _requestId = plan.Request.EventId;
        foreach (var monitor in changes.Open)
        {
            Open(plan, monitor, announce: announce && isNewRequest);
        }

        // The new windows come first: while one of ours is still on screen the process keeps the foreground, so the new
        // main window can take the keyboard; closing first would hand the foreground to another program.
        foreach (var window in gone)
        {
            Close(window);
        }

        // Windows that stay take the parts of the plan that can change while the tug is up.
        foreach (var window in _open)
        {
            window.Model.Update(plan);
        }

        StartTicking();
    }

    private void Open(TugPlan plan, TugMonitorPlan monitor, bool announce)
    {
        var model = new TugViewModel(plan, monitor.Role, _settings, _text, _time, _controller);
        Window window = monitor.Role == TugMonitorRole.Primary
            ? new PrimaryTugWindow(model, monitor, announce)
            : new MirrorTugWindow(model, monitor, BringPrimaryForward);
        _open.Add(new OpenWindow(window, model, monitor));
    }

    private void Close(OpenWindow open)
    {
        _open.Remove(open);
        (open.Window as PrimaryTugWindow)?.Retire();
        try
        {
            open.Window.Close();
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // The window was already closed by the system (its monitor disappeared); nothing is left to do.
        }
    }

    private void CloseAll()
    {
        foreach (var open in _open.ToList())
        {
            Close(open);
        }

        _requestId = null;
        StopTicking();
    }

    /// <summary>Settings changed: draw the same plan again in the new palette, backdrop and motion, without announcing it twice.</summary>
    private void Restyle()
    {
        if (_open.Count == 0)
        {
            return;
        }

        var plan = _controller.Plan;
        var old = _open.ToList();
        foreach (var open in old)
        {
            (open.Window as PrimaryTugWindow)?.Retire();
        }

        _open.Clear();
        _requestId = null;
        Apply(plan, announce: false);
        foreach (var open in old)
        {
            Close(open);
        }
    }

    private void BringPrimaryForward() => Primary?.TakeFocus();

    // ---- the countdown ------------------------------------------------------------------------------------------

    private void StartTicking() => _tick ??= _ticker.Start(() => Post(Tick));

    private void StopTicking()
    {
        _tick?.Dispose();
        _tick = null;
    }

    private void Tick()
    {
        foreach (var open in _open)
        {
            open.Model.Refresh();
        }
    }

    private sealed record OpenWindow(Window Window, TugViewModel Model, TugMonitorPlan Plan);
}
