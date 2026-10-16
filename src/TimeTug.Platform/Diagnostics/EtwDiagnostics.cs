using System.Diagnostics.Tracing;
using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Diagnostics;

/// <summary>Event Tracing for Windows provider <c>BinaryCompanion-TimeTug</c>. Never given event titles or calendar content.</summary>
[EventSource(Name = "BinaryCompanion-TimeTug")]
public sealed class EtwDiagnostics : EventSource, IDiagnostics
{
    /// <summary>The one instance (an EventSource name can be registered once per process).</summary>
    public static readonly EtwDiagnostics Log = new();

    private EtwDiagnostics()
    {
    }

    [Event(1, Level = EventLevel.Informational, Message = "{0}: {1}")]
    public void Info(string category, string message) => WriteEvent(1, category, message);

    [Event(2, Level = EventLevel.Warning, Message = "{0}: {1}")]
    public void Warning(string category, string message) => WriteEvent(2, category, message);

    [Event(3, Level = EventLevel.Error, Message = "{0}: {1}")]
    public void Error(string category, string message) => WriteEvent(3, category, message);

    public void Record(string level, string category, string message)
    {
        if (!IsEnabled())
        {
            return;
        }

        switch (level)
        {
            case "error":
                Error(category, message);
                break;
            case "warning":
                Warning(category, message);
                break;
            default:
                Info(category, message);
                break;
        }
    }
}
