using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.App.Flyout;

/// <summary>Adapts <see cref="FlyoutWindow"/> to what <see cref="FlyoutHost"/> needs.</summary>
internal sealed class FlyoutSurface : IFlyoutSurface
{
    public FlyoutSurface(FlyoutWindow window)
    {
        Window = window;
        window.WindowDeactivated += () => Deactivated?.Invoke();
        window.EscapeRequested += () => EscapePressed?.Invoke();
        window.WindowClosed += () => Closed?.Invoke();
        window.ContentRebuilt += () => ContentChanged?.Invoke();
    }

    public FlyoutWindow Window { get; }

    public event Action? Deactivated;

    public event Action? EscapePressed;

    public event Action? Closed;

    public event Action? ContentChanged;

    public SizeInDips MeasureContent() => Window.MeasureContent();

    public void Show(PixelRect bounds, FlyoutLook look) => Window.Present(bounds, look);

    public void Update(PixelRect bounds, FlyoutLook look) => Window.Reposition(bounds, look);

    public void Close() => Window.CloseFlyout();
}

/// <summary>Makes a flyout window around the one long-lived <see cref="FlyoutViewModel"/> each time the flyout opens.</summary>
internal sealed class FlyoutSurfaceFactory(FlyoutViewModel model, IUiDispatcher ui, ILocalizer text, IUiSettings settings) : IFlyoutSurfaceFactory
{
    /// <summary>The window made last (for the self-test).</summary>
    public FlyoutSurface? Last { get; private set; }

    public IFlyoutSurface Create()
    {
        var surface = new FlyoutSurface(new FlyoutWindow(model, ui, text, settings));
        Last = surface;
        return surface;
    }
}
