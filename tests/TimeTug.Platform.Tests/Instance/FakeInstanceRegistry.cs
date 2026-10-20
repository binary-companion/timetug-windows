using TimeTug.Platform.Instance;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Platform.Tests.Instance;

/// <summary>The machine's table of running copies: the first process to register a key holds it, later ones are redirected to it.</summary>
internal sealed class FakeInstanceRegistry : IInstanceRegistry
{
    private readonly Dictionary<string, FakeRegistration> _holders = [];

    public List<string> Keys { get; } = [];

    public int Redirects { get; private set; }

    /// <summary>Makes every redirect fail with this exception.</summary>
    public Exception? RedirectFails { get; set; }

    /// <summary>Makes every redirect wait for this task (never completing, for a hung primary).</summary>
    public Task? RedirectWaitsFor { get; set; }

    public IInstanceRegistration FindOrRegister(string key)
    {
        Keys.Add(key);
        if (_holders.TryGetValue(key, out var holder))
        {
            return new FakeRegistration(this, holder, isCurrent: false);
        }

        var registration = new FakeRegistration(this, null, isCurrent: true);
        _holders[key] = registration;
        registration.Key = key;
        return registration;
    }

    internal void Release(string key, FakeRegistration holder)
    {
        if (_holders.TryGetValue(key, out var current) && current == holder)
        {
            _holders.Remove(key);
        }
    }

    internal void CountRedirect() => Redirects++;

    internal sealed class FakeRegistration(FakeInstanceRegistry owner, FakeRegistration? holder, bool isCurrent) : IInstanceRegistration
    {
        public string Key { get; set; } = string.Empty;

        public bool Disposed { get; private set; }

        public bool IsCurrent { get; } = isCurrent;

        public event Action<ActivationKind>? ActivationReceived;

        public async Task RedirectToAsync(ActivationKind kind)
        {
            owner.CountRedirect();
            if (owner.RedirectFails is { } failure)
            {
                throw failure;
            }

            if (owner.RedirectWaitsFor is { } wait)
            {
                await wait;
            }

            holder?.ActivationReceived?.Invoke(kind);
        }

        public void Dispose()
        {
            Disposed = true;
            if (IsCurrent)
            {
                owner.Release(Key, this);
            }
        }
    }
}

internal sealed class FakeTug : ITugPresence
{
    private bool _showing;

    public bool IsTugShowing
    {
        get => _showing;
        set
        {
            _showing = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}
