namespace TimeTug.Platform.Instance;

/// <summary>
/// The fallback when the process has no package identity (the unit tests, an unpackaged run): the named mutex of <see cref="SingleInstanceGuard"/>.
/// A second launch learns that it is not the first and so never touches the first copy's data, but there is no way to reach the first
/// copy, so redirecting does nothing.
/// </summary>
public sealed class MutexInstanceRegistry(string identity) : IInstanceRegistry
{
    public IInstanceRegistration FindOrRegister(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return new Registration(SingleInstanceGuard.TryAcquire(SingleInstanceGuard.NameFor(identity + "." + key)));
    }

    private sealed class Registration(SingleInstanceGuard? guard) : IInstanceRegistration
    {
        public bool IsCurrent => guard is not null;

        public event Action<ActivationKind>? ActivationReceived
        {
            add { }
            remove { }
        }

        public Task RedirectToAsync(ActivationKind kind) => Task.CompletedTask;

        public void Dispose() => guard?.Dispose();
    }
}
