using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Diagnostics;

/// <summary>Sends every entry to each sink; one failing sink never stops the others.</summary>
public sealed class FanOutDiagnostics : IDiagnostics
{
    private readonly IDiagnostics[] _sinks;

    public FanOutDiagnostics(params IDiagnostics[] sinks) => _sinks = sinks;

    /// <summary>The app's standard diagnostics: the rolling file log plus ETW.</summary>
    public static FanOutDiagnostics CreateDefault() => new(new RollingFileLog(RollingFileLog.DefaultDirectory()), EtwDiagnostics.Log);

    public void Record(string level, string category, string message)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                sink.Record(level, category, message);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Diagnostics must never throw into the caller.
            }
        }
    }
}
