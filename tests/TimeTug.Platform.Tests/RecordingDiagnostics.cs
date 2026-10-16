using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Tests;

internal sealed class RecordingDiagnostics : IDiagnostics
{
    private readonly List<(string Level, string Category, string Message)> _entries = [];

    public IReadOnlyList<(string Level, string Category, string Message)> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public void Record(string level, string category, string message)
    {
        lock (_entries)
        {
            _entries.Add((level, category, message));
        }
    }
}
