using TimeTug.Presentation.Abstractions;
using TimeTug.Presentation.Flyout;

namespace TimeTug.Platform.Instance;

/// <summary>
/// Keeps TimeTug to one running copy per package identity and lets a later launch ask the running copy to open the flyout. It sits on
/// an <see cref="IInstanceRegistry"/> (the Windows App SDK's <c>AppInstance</c> in the package, a mutex without identity).
/// <list type="bullet">
/// <item>The first launch registers the key and is the primary. A later launch finds the key held, hands its activation to the
/// primary and exits: it must not start the engine or touch the data folder the primary is using.</item>
/// <item>The primary subscribes when this service is made, which is before the engine starts. An activation that arrives before
/// <see cref="MarkReady"/> is held (one at most) and delivered when startup has finished.</item>
/// <item>While the tug is showing, an activation is ignored and logged: the tug has priority. A burst of launches within
/// <see cref="CoalesceWindow"/> is one activation.</item>
/// </list>
/// Activations are raised on an arbitrary thread; the subscriber posts to the UI thread.
/// </summary>
public sealed class AppInstanceService : IAppInstance
{
    /// <summary>The registration key every copy of the app uses.</summary>
    public const string MainKey = "TimeTug.Main";

    /// <summary>Launches this close together are one activation.</summary>
    public static readonly TimeSpan CoalesceWindow = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a second launch waits for the primary to take its activation before it gives up and exits anyway.</summary>
    public static readonly TimeSpan RedirectTimeout = TimeSpan.FromSeconds(10);

    private const string Category = "instance";

    private readonly IInstanceRegistration _registration;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITugPresence? _tug;
    private bool _ready;
    private bool _disposed;
    private ActivationKind? _held;
    // A monotonic timestamp, not a wall-clock instant: a clock set back must not make every later launch look like part of the last burst.
    private long? _lastDelivered;

    /// <summary>Registers (or finds) <paramref name="key"/>. This is the moment that decides whether the process is the primary.</summary>
    public AppInstanceService(IInstanceRegistry registry, string key, TimeProvider? time = null, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _time = time ?? TimeProvider.System;
        Diagnostics = diagnostics;
        _registration = registry.FindOrRegister(key);
        if (_registration.IsCurrent)
        {
            _registration.ActivationReceived += OnReceived;
        }
    }

    /// <summary>The registry for this process: the system's when it has package identity (falling back to the mutex when the system refuses), the mutex alone otherwise.</summary>
    public static IInstanceRegistry DefaultRegistry(string? packageFamilyName, IDiagnostics? diagnostics) =>
        packageFamilyName is null
            ? new MutexInstanceRegistry("unpackaged")
            : new FallbackInstanceRegistry(new WindowsInstanceRegistry(), new MutexInstanceRegistry(packageFamilyName), diagnostics);

    /// <summary>The service for this process (see <see cref="DefaultRegistry"/>).</summary>
    public static AppInstanceService CreateDefault(string? packageFamilyName, IDiagnostics? diagnostics) =>
        new(DefaultRegistry(packageFamilyName, diagnostics), MainKey, TimeProvider.System, diagnostics);

    /// <summary>Where this service writes. A second launch starts with a sink that touches no files; the primary switches to the log once it knows it is the primary.</summary>
    public IDiagnostics? Diagnostics { get; set; }

    public bool IsPrimary => _registration.IsCurrent;

    public event Action<ActivationKind>? Activated;

    public async Task RedirectAsync(ActivationKind kind)
    {
        if (IsPrimary)
        {
            Note("info", "this is the first copy; there is nothing to redirect to");
            return;
        }

        try
        {
            await _registration.RedirectToAsync(kind).WaitAsync(RedirectTimeout, _time).ConfigureAwait(false);
            Note("info", "this launch was handed to the running copy");
        }
        catch (TimeoutException)
        {
            Note("warning", $"the running copy did not answer within {RedirectTimeout.TotalSeconds:0} seconds; this launch exits anyway");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Whatever went wrong (the primary is closing, the call was refused), this launch must still not start a second copy.
            Note("warning", $"handing this launch to the running copy failed ({e.GetType().Name}); this launch exits anyway");
        }
    }

    public void MarkReady(ITugPresence? tug = null)
    {
        ActivationKind? held;
        lock (_gate)
        {
            if (_ready || _disposed)
            {
                return;
            }

            _ready = true;
            _tug = tug;
            held = _held;
            _held = null;
        }

        if (held is { } kind)
        {
            Deliver(kind);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _held = null;
        }

        _registration.ActivationReceived -= OnReceived;
        _registration.Dispose();
    }

    private void OnReceived(ActivationKind kind)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (!_ready)
            {
                Note("info", "a second launch arrived before startup finished; it is held until then");
                _held = kind;
                return;
            }
        }

        Deliver(kind);
    }

    private void Deliver(ActivationKind kind)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_tug?.IsTugShowing == true)
            {
                Note("info", "a second launch was ignored because the tug is showing");
                return;
            }

            var now = _time.GetTimestamp();
            if (_lastDelivered is { } last && _time.GetElapsedTime(last, now) < CoalesceWindow)
            {
                Note("info", "a second launch was folded into the one just before");
                return;
            }

            _lastDelivered = now;
            Note("info", "a second launch asked this copy to open the flyout");
        }

        try
        {
            Activated?.Invoke(kind);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Note("error", $"handling a second launch failed ({e.GetType().Name})");
        }
    }

    private void Note(string level, string message) => Diagnostics?.Record(level, Category, message);
}
