using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Instance;

/// <summary>Uses the preferred registry and, when the system refuses it (no package identity, a broken runtime), the fallback, saying so in the log.</summary>
public sealed class FallbackInstanceRegistry(IInstanceRegistry preferred, IInstanceRegistry fallback, IDiagnostics? diagnostics = null) : IInstanceRegistry
{
    public IInstanceRegistration FindOrRegister(string key)
    {
        try
        {
            return preferred.FindOrRegister(key);
        }
        catch (Exception e) when (e is InvalidOperationException or System.Runtime.InteropServices.COMException or TypeInitializationException or DllNotFoundException or EntryPointNotFoundException or NotSupportedException)
        {
            diagnostics?.Record("info", "instance", $"the system instance registry is not available ({e.GetType().Name}); using the mutex guard");
            return fallback.FindOrRegister(key);
        }
    }
}
