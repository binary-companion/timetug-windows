using TimeTug.Presentation.Abstractions;

namespace TimeTug.Presentation.Startup;

/// <summary>
/// The order in which the running app lets go of things when the person chooses Quit:
/// <list type="number">
/// <item>the flyout closes (and stays closed), so nothing the person is looking at outlives the quit;</item>
/// <item>an outstanding takeover is closed, so the engine is never left waiting for an answer;</item>
/// <item>the notification-area icon is removed, so a quit never leaves a ghost icon;</item>
/// <item>the engine is disposed last, after everything that talks to it has stopped.</item>
/// </list>
/// A step that fails is recorded and the next one still runs: a quit must always get to the end. The sequence runs once; a second
/// call returns the first one's task.
/// </summary>
public sealed class QuitSequence
{
    private const string Category = "shutdown";

    private readonly Action _closeFlyout;
    private readonly Func<Task> _closeTakeover;
    private readonly Action _removeTrayIcon;
    private readonly Func<Task> _disposeEngine;
    private readonly IDiagnostics? _diagnostics;
    private readonly object _gate = new();
    private Task? _run;

    public QuitSequence(Action closeFlyout, Func<Task> closeTakeover, Action removeTrayIcon, Func<Task> disposeEngine, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(closeFlyout);
        ArgumentNullException.ThrowIfNull(closeTakeover);
        ArgumentNullException.ThrowIfNull(removeTrayIcon);
        ArgumentNullException.ThrowIfNull(disposeEngine);
        _closeFlyout = closeFlyout;
        _closeTakeover = closeTakeover;
        _removeTrayIcon = removeTrayIcon;
        _disposeEngine = disposeEngine;
        _diagnostics = diagnostics;
    }

    /// <summary>Runs the four steps in order. Never throws.</summary>
    public Task RunAsync()
    {
        lock (_gate)
        {
            return _run ??= RunCoreAsync();
        }
    }

    private async Task RunCoreAsync()
    {
        await Step("the flyout", () =>
        {
            _closeFlyout();
            return Task.CompletedTask;
        });
        await Step("the outstanding takeover", _closeTakeover);
        await Step("the tray icon", () =>
        {
            _removeTrayIcon();
            return Task.CompletedTask;
        });
        await Step("the engine", _disposeEngine);
    }

    private async Task Step(string name, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            // The type only: a message could carry calendar content.
            _diagnostics?.Record("error", Category, $"closing {name} raised {e.GetType().Name}");
        }
    }
}
