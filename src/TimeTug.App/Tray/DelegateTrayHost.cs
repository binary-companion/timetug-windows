using TimeTug.Presentation.Tray;

namespace TimeTug.App.Tray;

/// <summary>What the tray asks of the rest of the app, as delegates the shell supplies.</summary>
internal sealed class DelegateTrayHost(Action showWelcome, Action toggleFlyout, Func<ValueTask> testTug, Func<ValueTask> quit) : ITrayHost
{
    public void ShowWelcome() => showWelcome();

    public void ToggleFlyout() => toggleFlyout();

    public ValueTask TestTugAsync() => testTug();

    public ValueTask QuitAsync() => quit();
}
