#if SELFTEST
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Startup;

namespace TimeTug.App.SelfTest;

/// <summary>
/// A startup task a scenario controls: it holds a state, behaves like Windows (only a plainly disabled task can be enabled, a policy cannot be
/// switched off) and records the requests. Scenarios that draw the Settings window use it so none of them touches the machine's real task;
/// <c>settings-general</c> reads the real one separately.
/// </summary>
internal sealed class ScriptedStartupTask : IStartupTaskService
{
    public StartupState State { get; set; } = StartupState.Disabled;

    /// <summary>When set, a request to enable leaves the task in this state (the person or a policy refuses).</summary>
    public StartupState? StateAfterRequest { get; set; }

    /// <summary>When set, a request to change the task fails with this error.</summary>
    public Exception? ChangeThrows { get; set; }

    public List<string> Calls { get; } = [];

    public Task<StartupState> GetStateAsync()
    {
        Calls.Add("get");
        return Task.FromResult(State);
    }

    public Task<StartupState> RequestEnableAsync()
    {
        Calls.Add("enable");
        if (ChangeThrows is { } error)
        {
            return Task.FromException<StartupState>(error);
        }

        if (StateAfterRequest is { } refused)
        {
            State = refused;
        }
        else if (State == StartupState.Disabled)
        {
            State = StartupState.Enabled;
        }

        return Task.FromResult(State);
    }

    public Task<StartupState> DisableAsync()
    {
        Calls.Add("disable");
        if (ChangeThrows is { } error)
        {
            return Task.FromException<StartupState>(error);
        }

        if (State == StartupState.Enabled)
        {
            State = StartupState.Disabled;
        }

        return Task.FromResult(State);
    }
}

/// <summary>Opens nothing: records the links the app chose and answers as a scenario says.</summary>
internal sealed class RecordingSystemLauncher : ISystemLauncher
{
    public List<string> Opened { get; } = [];

    public bool Succeeds { get; set; } = true;

    public ValueTask<bool> LaunchAsync(string url)
    {
        Opened.Add(url);
        return ValueTask.FromResult(Succeeds);
    }
}
#endif
