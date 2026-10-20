using TimeTug.Platform.Hotkey;
using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Settings;

namespace TimeTug.Platform.Tests.Hotkey;

/// <summary>A registrar that behaves like Windows: one shortcut per registrar, refused when another program holds the shortcut, and it records everything.</summary>
internal sealed class FakeRegistrar : IHotkeyRegistrar
{
    public List<string> Calls { get; } = [];

    /// <summary>Shortcuts another program holds.</summary>
    public HashSet<HotkeyBinding> HeldElsewhere { get; } = [];

    public HotkeyBinding? Active { get; private set; }

    /// <summary>The largest number of shortcuts held at one moment (never more than one is allowed).</summary>
    public int MostHeld { get; private set; }

    public int Registrations => Calls.Count(c => c.StartsWith("register ", StringComparison.Ordinal));

    public Exception? RegisterThrows { get; set; }

    public bool Disposed { get; private set; }

    public event Action? Pressed;

    public Task<bool> RegisterAsync(HotkeyBinding binding)
    {
        Calls.Add("register " + binding.ToText());
        if (RegisterThrows is { } failure)
        {
            throw failure;
        }

        if (Active is not null)
        {
            throw new InvalidOperationException("a second shortcut was registered while one is held");
        }

        if (HeldElsewhere.Contains(binding))
        {
            return Task.FromResult(false);
        }

        Active = binding;
        MostHeld = Math.Max(MostHeld, 1);
        return Task.FromResult(true);
    }

    public Task UnregisterAsync()
    {
        Calls.Add("unregister");
        Active = null;
        return Task.CompletedTask;
    }

    public void Press() => Pressed?.Invoke();

    public void Dispose() => Disposed = true;
}

internal sealed class FakeStore : IHotkeyStore
{
    public HotkeyState Current { get; set; } = HotkeyState.None;

    public int Saves { get; private set; }

    public HotkeyState Load() => Current;

    public void Save(HotkeyState state)
    {
        Current = state;
        Saves++;
    }
}

internal sealed class ImmediateDispatcher : IUiDispatcher
{
    public bool HasThreadAccess => true;

    public void Post(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
