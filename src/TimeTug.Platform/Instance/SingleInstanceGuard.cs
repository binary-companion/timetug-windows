namespace TimeTug.Platform.Instance;

/// <summary>
/// A named mutex that keeps a second copy of the app from touching the engine's data while the first one runs. Real single
/// instance behaviour (bringing the first window forward, handling activation) is slice 2; this only makes a second launch safe:
/// it cannot acquire the guard, shows nothing and exits. The mutex is per Windows session (<c>Local\</c>) and named after the
/// package family, so builds with different identities and separate data do not block each other.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly int _ownerThread;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
        _ownerThread = Environment.CurrentManagedThreadId;
    }

    /// <summary>The mutex name for an identity such as a package family name. Anything but letters, digits, dots, dashes and underscores becomes an underscore.</summary>
    public static string NameFor(string identity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        var clean = new string(identity.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());
        return @"Local\TimeTug.SingleInstance." + clean;
    }

    /// <summary>The guard when this is the first instance of <paramref name="name"/>, or null when another one holds it.</summary>
    public static SingleInstanceGuard? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            return new SingleInstanceGuard(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Releases the guard. A mutex can only be released by the thread that took it; from another thread the handle is just closed (the system frees it when the process ends).</summary>
    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId == _ownerThread)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Not owned any more; nothing to release.
            }
        }

        _mutex.Dispose();
    }
}
