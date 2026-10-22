#if SELFTEST
using TimeTug.Presentation.Settings;

namespace TimeTug.App.SelfTest;

/// <summary>
/// The popup shortcut for scenarios that only draw the Settings window: a state a scenario sets, answers it chooses, and a log of what the
/// recorder asked. No Windows registration is involved (<c>settings-general</c> uses the real <c>HotkeyService</c> instead).
/// </summary>
internal sealed class ScriptedHotkeys : IHotkeyControl
{
    public HotkeyState State { get; private set; } = HotkeyState.None;

    public List<string> Calls { get; } = [];

    public bool Suspended { get; private set; }

    /// <summary>What a request to set a shortcut answers; by default it registers.</summary>
    public Func<HotkeyBinding, HotkeyResult>? Answer { get; set; }

    public event Action? StateChanged;

    public void Show(HotkeyState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

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
        Suspended = false;
        var now = DateTimeOffset.UtcNow;
        if (binding is not { } wanted)
        {
            Show(new HotkeyState(null, HotkeyStatus.Cleared, now));
            return Task.FromResult(HotkeyResult.Cleared);
        }

        var result = Answer?.Invoke(wanted) ?? HotkeyResult.Registered;
        switch (result.Status)
        {
            case HotkeyStatus.Registered:
                Show(new HotkeyState(wanted, HotkeyStatus.Registered, now));
                break;
            case HotkeyStatus.Conflict:
                Show(new HotkeyState(State.Binding, HotkeyStatus.Conflict, now, wanted));
                break;
        }

        return Task.FromResult(result);
    }
}
#endif
