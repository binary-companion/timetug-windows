using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;
using TimeTug.Presentation.Startup;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>A startup task whose state a test sets. Requests behave like Windows: only a disabled task can be enabled.</summary>
internal sealed class FakeStartupTask : IStartupTaskService
{
    public StartupState State { get; set; } = StartupState.Disabled;

    /// <summary>When set, <see cref="RequestEnableAsync"/> leaves the task in this state (the person or a policy refuses).</summary>
    public StartupState? StateAfterRequest { get; set; }

    /// <summary>Makes every call throw.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Makes only a request to change the task throw (reading still works).</summary>
    public Exception? ChangeThrows { get; set; }

    public TaskCompletionSource? Gate { get; set; }

    public List<string> Calls { get; } = [];

    public Task<StartupState> GetStateAsync()
    {
        Calls.Add("get");
        return Throws is { } e ? Task.FromException<StartupState>(e) : Task.FromResult(State);
    }

    public async Task<StartupState> RequestEnableAsync()
    {
        Calls.Add("enable");
        if (Gate is { } gate)
        {
            await gate.Task;
        }

        if ((ChangeThrows ?? Throws) is { } e)
        {
            throw e;
        }

        if (StateAfterRequest is { } refused)
        {
            State = refused;
        }
        else if (State == StartupState.Disabled)
        {
            State = StartupState.Enabled;
        }

        return State;
    }

    public Task<StartupState> DisableAsync()
    {
        Calls.Add("disable");
        if ((ChangeThrows ?? Throws) is { } e)
        {
            return Task.FromException<StartupState>(e);
        }

        if (State == StartupState.Enabled)
        {
            State = StartupState.Disabled;
        }

        return Task.FromResult(State);
    }
}

/// <summary>Records the links the app opens and can refuse them.</summary>
internal sealed class FakeSystemLauncher : ISystemLauncher
{
    public List<string> Opened { get; } = [];

    public bool Succeeds { get; set; } = true;

    public ValueTask<bool> LaunchAsync(string url)
    {
        Opened.Add(url);
        return ValueTask.FromResult(Succeeds);
    }
}
