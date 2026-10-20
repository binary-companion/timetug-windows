using System.Drawing;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using TimeTug.Platform.Tray;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Tray;

namespace TimeTug.App.Tray;

/// <summary>
/// The notification-area icon: shows what <see cref="TrayViewModel"/> says (icon, tooltip, menu) and forwards clicks to it.
/// Right-click shows the menu; a left click is the view model's primary click. Must be created, used and disposed on the UI
/// thread; the view model's changes may arrive from any thread and are posted here.
/// <para>
/// The icon picture (the quiet, colour or attention puppy with the minute digit in the last ten minutes) is drawn by
/// <see cref="TrayIconRenderer"/> and kept equal to the plan by <see cref="TrayIconController"/>, which sets the icon and the
/// tooltip only when they differ. A new plan arrives when the engine's state changes, on the minute ticker, when the taskbar
/// theme changes and when attention changes; nothing here polls.
/// </para>
/// </summary>
internal sealed class TrayIconService : IDisposable, ITrayIconSink
{
    private const string Category = "tray";

    private readonly TrayViewModel _model;
    private readonly IUiDispatcher _ui;
    private readonly IDiagnostics _diagnostics;
    private readonly TrayIconController _controller;
    private TaskbarIcon? _icon;
    private bool _disposed;

    public TrayIconService(TrayViewModel model, IUiDispatcher ui, IDiagnostics diagnostics, string? assetFolder = null)
    {
        _model = model;
        _ui = ui;
        _diagnostics = diagnostics;
        var folder = assetFolder ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Tray");
        _controller = new TrayIconController(new TrayIconRenderer(new TrayIconFiles(folder)), this, TrayIconMetrics.SmallIconSize, diagnostics);
    }

    /// <summary>True once Windows accepted the icon into the notification area.</summary>
    public bool IsCreated => _icon is { IsCreated: true };

    /// <summary>The picture the tray shows now (its width is the icon size in pixels), or null before the first one.</summary>
    public TrayIconImage? AppliedIcon => _controller.Icon;

    /// <summary>The tooltip the tray shows now.</summary>
    public string? AppliedTooltip => _controller.Tooltip;

    /// <summary>Creates the icon. A failure is recorded and leaves the app running without a tray icon (the person can still quit from Task Manager); it never throws.</summary>
    public void Start()
    {
        try
        {
            var icon = new TaskbarIcon
            {
                MenuActivation = PopupActivationMode.RightClick,
                ContextMenuMode = ContextMenuMode.PopupMenu,
                NoLeftClickDelay = true,
                LeftClickCommand = new RelayCommand(_model.PrimaryClick),
            };
            _icon = icon;
            Apply(_model.Current);
            // Follow the plan even when Windows cannot take the icon yet (no taskbar, as on the hosted ARM64 runner): the library
            // creates it when a taskbar appears, and it must then show the current plan, not the first one.
            _model.Changed += OnChanged;
            // The efficiency mode the library offers would throttle the whole process, including the engine; keep it off.
            icon.ForceCreate(enablesEfficiencyMode: false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _diagnostics.Record("error", Category, $"the tray icon could not be created ({e.GetType().Name}: {e.Message})");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model.Changed -= OnChanged;
        try
        {
            _icon?.Dispose();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _diagnostics.Record("warning", Category, $"the tray icon did not close cleanly ({e.GetType().Name})");
        }

        _icon = null;
    }

    private void OnChanged() => _ui.Post(() =>
    {
        if (!_disposed)
        {
            Apply(_model.Current);
        }
    });

    private void Apply(TrayView view)
    {
        if (_icon is not { } icon)
        {
            return;
        }

        _controller.Apply(view.Icon, view.Tooltip);
        icon.ContextFlyout = TrayMenu.Build(view, CommandFor);
    }

    void ITrayIconSink.SetIcon(TrayIconImage image)
    {
        if (_icon is not { } icon)
        {
            return;
        }

        // The library disposes the icon it replaces, so every change gets a new Icon object.
        using var stream = new MemoryStream(image.ToIco());
        icon.Icon = new Icon(stream);
    }

    void ITrayIconSink.SetTooltip(string text)
    {
        if (_icon is { } icon)
        {
            icon.ToolTipText = text;
        }
    }

    private RelayCommand CommandFor(TrayCommand command) => new(() => _ = RunAsync(command));

    private async Task RunAsync(TrayCommand command)
    {
        try
        {
            await _model.InvokeAsync(command);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _diagnostics.Record("error", Category, $"the {command} menu command failed ({e.GetType().Name})");
        }
    }
}
