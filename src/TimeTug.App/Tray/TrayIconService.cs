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
/// </summary>
internal sealed class TrayIconService : IDisposable
{
    private const string Category = "tray";

    private readonly TrayViewModel _model;
    private readonly IUiDispatcher _ui;
    private readonly IDiagnostics _diagnostics;
    private readonly string _assetFolder;
    private TaskbarIcon? _icon;
    private string? _appliedIcon;
    private string? _appliedTooltip;
    private int _iconSize;
    private bool _disposed;

    public TrayIconService(TrayViewModel model, IUiDispatcher ui, IDiagnostics diagnostics, string? assetFolder = null)
    {
        _model = model;
        _ui = ui;
        _diagnostics = diagnostics;
        _assetFolder = assetFolder ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Tray");
    }

    /// <summary>True once Windows accepted the icon into the notification area.</summary>
    public bool IsCreated => _icon is { IsCreated: true };

    /// <summary>The icon file the tray shows now (for example <c>tray-dark.ico</c>), or null before it is created.</summary>
    public string? AppliedIconFile => _appliedIcon;

    /// <summary>The tooltip the tray shows now.</summary>
    public string? AppliedTooltip => _appliedTooltip;

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
            // The efficiency mode the library offers would throttle the whole process, including the engine; keep it off.
            icon.ForceCreate(enablesEfficiencyMode: false);
            _model.Changed += OnChanged;
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

        if (view.Tooltip != _appliedTooltip)
        {
            icon.ToolTipText = view.Tooltip;
            _appliedTooltip = view.Tooltip;
        }

        var size = TrayIconMetrics.SmallIconSize();
        if (view.Icon.FileName != _appliedIcon || size != _iconSize)
        {
            if (LoadIcon(view.Icon.FileName, size) is { } picture)
            {
                // The library disposes the icon it replaces, so every change gets a new Icon object.
                icon.Icon = picture;
                _appliedIcon = view.Icon.FileName;
                _iconSize = size;
            }
        }

        icon.ContextFlyout = TrayMenu.Build(view, CommandFor);
    }

    private Icon? LoadIcon(string fileName, int size)
    {
        try
        {
            return new Icon(Path.Combine(_assetFolder, fileName), size, size);
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            _diagnostics.Record("error", Category, $"the tray icon file {fileName} could not be loaded ({e.GetType().Name})");
            return null;
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
