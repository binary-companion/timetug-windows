using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Tray;

namespace TimeTug.Platform.Tray;

/// <summary>Where the controller puts the icon and the tooltip (the notification-area icon in the app).</summary>
public interface ITrayIconSink
{
    void SetIcon(TrayIconImage image);

    void SetTooltip(string text);
}

/// <summary>
/// Keeps the tray icon and its tooltip equal to the tray plan (<see cref="TrayIconView"/> and tooltip), drawing the icon with
/// <see cref="TrayIconRenderer"/> and changing the icon and the tooltip only when each differs from what is showing. It is called
/// when the plan changes (the engine's state, the minute ticker, the taskbar theme, attention) and never polls. A picture that
/// cannot be drawn is recorded and the old icon stays; the next call tries again. Use it from one thread (the UI thread).
/// </summary>
public sealed class TrayIconController
{
    private const string Category = "tray";

    private readonly TrayIconRenderer _renderer;
    private readonly ITrayIconSink _sink;
    private readonly Func<int> _iconSize;
    private readonly IDiagnostics _diagnostics;
    private (TrayIconView View, int Size)? _appliedKey;

    public TrayIconController(TrayIconRenderer renderer, ITrayIconSink sink, Func<int> iconSize, IDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(sink);
        ArgumentNullException.ThrowIfNull(iconSize);
        ArgumentNullException.ThrowIfNull(diagnostics);
        _renderer = renderer;
        _sink = sink;
        _iconSize = iconSize;
        _diagnostics = diagnostics;
    }

    /// <summary>The picture the tray shows now, or null before the first one.</summary>
    public TrayIconImage? Icon { get; private set; }

    /// <summary>The tooltip the tray shows now, or null before the first one.</summary>
    public string? Tooltip { get; private set; }

    /// <summary>Makes the tray show <paramref name="icon"/> and <paramref name="tooltip"/>; what already matches is left alone.</summary>
    public void Apply(TrayIconView icon, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(icon);
        ArgumentNullException.ThrowIfNull(tooltip);
        if (tooltip != Tooltip)
        {
            _sink.SetTooltip(tooltip);
            Tooltip = tooltip;
        }

        int size;
        try
        {
            size = TrayIconRenderer.NearestSize(_iconSize());
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Fail(icon, e);
            return;
        }

        if (_appliedKey is { } applied && applied.View == icon && applied.Size == size)
        {
            return;
        }

        TrayIconImage image;
        try
        {
            image = _renderer.Render(icon.State, icon.Digit, icon.LightTaskbar, size);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Fail(icon, e);
            return;
        }

        _sink.SetIcon(image);
        Icon = image;
        _appliedKey = (icon, size);
    }

    // The message names the icon state only: never a meeting title (the tooltip carries those) and never a path.
    private void Fail(TrayIconView icon, Exception e) =>
        _diagnostics.Record("error", Category, $"the {icon.State} tray icon could not be drawn ({e.GetType().Name})");
}
