using TimeTug.Presentation.Settings;

namespace TimeTug.Presentation.Tests.Settings;

/// <summary>The popup shortcut as the recorder sees it: a state, a log of what it was asked to do, and results a test chooses.</summary>
internal sealed class FakeHotkeys : IHotkeyControl
{
    public FakeHotkeys(HotkeyState? state = null) => State = state ?? HotkeyState.None;

    public HotkeyState State { get; set; }

    public List<string> Calls { get; } = [];

    /// <summary>Whether the current registration is released right now (suspended and not yet resumed).</summary>
    public bool Suspended { get; private set; }

    /// <summary>What <see cref="SetAsync"/> answers for a shortcut; by default it registers.</summary>
    public Func<HotkeyBinding, HotkeyResult>? Answer { get; set; }

    public Exception? SetThrows { get; set; }

    public event Action? StateChanged;

    public Task SuspendForRecording()
    {
        Calls.Add("suspend");
        Suspended = true;
        return Task.CompletedTask;
    }

    public Task Resume()
    {
        Calls.Add("resume");
        Suspended = false;
        return Task.CompletedTask;
    }

    public Task<HotkeyResult> SetAsync(HotkeyBinding? binding)
    {
        Calls.Add("set:" + (binding?.ToText() ?? "none"));
        // Setting a shortcut ends a suspension, as the real service does.
        Suspended = false;
        if (SetThrows is { } e)
        {
            return Task.FromException<HotkeyResult>(e);
        }

        if (binding is not { } wanted)
        {
            State = new HotkeyState(null, HotkeyStatus.Cleared, DateTimeOffset.UnixEpoch);
            StateChanged?.Invoke();
            return Task.FromResult(HotkeyResult.Cleared);
        }

        var result = Answer?.Invoke(wanted) ?? (wanted.Validate() is var reason and not HotkeyReason.None ? HotkeyResult.Invalid(reason) : HotkeyResult.Registered);
        State = result.Status switch
        {
            HotkeyStatus.Registered => new HotkeyState(wanted, HotkeyStatus.Registered, DateTimeOffset.UnixEpoch),
            HotkeyStatus.Conflict => new HotkeyState(State.Binding, HotkeyStatus.Conflict, DateTimeOffset.UnixEpoch, wanted),
            _ => State,
        };
        if (result.Status != HotkeyStatus.Invalid)
        {
            StateChanged?.Invoke();
        }

        return Task.FromResult(result);
    }

    public void RaiseStateChanged() => StateChanged?.Invoke();
}
