using TimeTug.Platform.Hotkey;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Platform.Instance;

/// <summary>
/// The small hooks that connect a trigger to the flyout, so the composition root (and the self-test) wire them the same way. Each
/// returns the subscription; dispose it to disconnect.
/// </summary>
public static class FlyoutTriggers
{
    /// <summary>
    /// A second launch opens the flyout in this copy (<see cref="FlyoutHost.Show"/>, not <c>Toggle</c>: a second launch never closes an
    /// open flyout). The activation arrives on an arbitrary thread; the opening is posted to the UI thread.
    /// </summary>
    public static IDisposable OpenOnActivation(IAppInstance instance, FlyoutHost host, IUiDispatcher ui)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(ui);
        void OnActivated(ActivationKind kind)
        {
            if (kind == ActivationKind.Flyout)
            {
                ui.Post(() => host.Show());
            }
        }

        instance.Activated += OnActivated;
        return new Subscription(() => instance.Activated -= OnActivated);
    }

    /// <summary>The global shortcut toggles the flyout (<see cref="FlyoutHost.Toggle"/>), which stays closed while a tug shows. <see cref="HotkeyService.Pressed"/> is already on the UI thread and already skips a showing tug.</summary>
    public static IDisposable ToggleOnHotkey(HotkeyService hotkey, FlyoutHost host)
    {
        ArgumentNullException.ThrowIfNull(hotkey);
        ArgumentNullException.ThrowIfNull(host);
        void OnPressed() => host.Toggle();
        hotkey.Pressed += OnPressed;
        return new Subscription(() => hotkey.Pressed -= OnPressed);
    }

    private sealed class Subscription(Action undo) : IDisposable
    {
        private Action? _undo = undo;

        public void Dispose() => Interlocked.Exchange(ref _undo, null)?.Invoke();
    }
}
