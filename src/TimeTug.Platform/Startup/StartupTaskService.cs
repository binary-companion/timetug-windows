using TimeTug.Presentation.Startup;
using Windows.ApplicationModel;

namespace TimeTug.Platform.Startup;

/// <summary>One startup task, as far as the service needs it (<see cref="StartupTask"/> in production, a fake in the tests).</summary>
public interface IStartupTaskHandle
{
    StartupTaskState State { get; }

    Task<StartupTaskState> RequestEnableAsync();

    void Disable();
}

/// <summary>
/// Launch at login over <see cref="StartupTask"/>, the startup task the package manifest declares (<see cref="TaskId"/>). Windows keeps the
/// answer: this class stores nothing. Every call opens the task afresh, so a change the person made in Settings or Task Manager is seen at
/// once, and every call returns the state Windows reports after it, which stays disabled when the person or a policy has turned the task
/// off. A failure of the call (no package identity, the task missing from the manifest) is an exception for the caller to show.
/// </summary>
public sealed class StartupTaskService : IStartupTaskService
{
    /// <summary>The <c>TaskId</c> of the startup task in <c>Package.appxmanifest</c>.</summary>
    public const string TaskId = "TimeTugStartup";

    private readonly Func<Task<IStartupTaskHandle>> _open;

    public StartupTaskService(Func<Task<IStartupTaskHandle>>? open = null) => _open = open ?? OpenManifestTaskAsync;

    /// <summary>The Windows enum to the app's: the same five values.</summary>
    public static StartupState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled => StartupState.Enabled,
        StartupTaskState.Disabled => StartupState.Disabled,
        StartupTaskState.DisabledByUser => StartupState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupState.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => StartupState.EnabledByPolicy,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Windows reported a startup task state this app does not know."),
    };

    public async Task<StartupState> GetStateAsync() => Map((await _open().ConfigureAwait(false)).State);

    public async Task<StartupState> RequestEnableAsync()
    {
        var task = await _open().ConfigureAwait(false);
        return Map(await task.RequestEnableAsync().ConfigureAwait(false));
    }

    public async Task<StartupState> DisableAsync()
    {
        var task = await _open().ConfigureAwait(false);
        task.Disable();
        // Read it again: a policy-enabled task ignores Disable and the caller must see that.
        return Map(task.State);
    }

    private static async Task<IStartupTaskHandle> OpenManifestTaskAsync() => new Handle(await StartupTask.GetAsync(TaskId));

    private sealed class Handle(StartupTask task) : IStartupTaskHandle
    {
        public StartupTaskState State => task.State;

        public async Task<StartupTaskState> RequestEnableAsync() => await task.RequestEnableAsync();

        public void Disable() => task.Disable();
    }
}
