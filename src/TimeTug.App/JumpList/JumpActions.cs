using Microsoft.Extensions.Hosting;
using TimeTug.Presentation.JumpList;

namespace TimeTug.App.JumpList;

/// <summary>
/// What the jump list's actions do in the running app: the flyout opens (and stays open if it is), Join goes through the flyout's own path (a
/// link that could not be opened shows the flyout with its copy-link state), Test tug is the tray menu's, Settings opens the Settings window.
/// The functions are the shell's, so this holds no state.
/// </summary>
internal sealed class JumpActions(Action showFlyout, Func<string, ValueTask<bool>> join, Func<ValueTask> testTug, Action openSettings) : IJumpActions
{
    public void ShowFlyout() => showFlyout();

    public async ValueTask JoinAsync(string eventId)
    {
        _ = await join(eventId);
    }

    public ValueTask TestTugAsync() => testTug();

    public void OpenSettings() => openSettings();
}

/// <summary>
/// Saves the jump list's base list when the host starts (before the engine's first state, because Windows keeps the list on disk and a
/// stale item may be left from a run that did not quit cleanly) and again, without "Join next meeting", when the host stops.
/// </summary>
internal sealed class JumpListHostedService(JumpListService service) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        service.Start();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => service.StopAsync();
}
