namespace TimeTug.Presentation.Diagnostics;

/// <summary>The app's diagnostics log as the Accounts pane reads it (the Platform's rolling files).</summary>
public interface IDiagnosticsReader
{
    /// <summary>
    /// The last <paramref name="maxLines"/> lines of the log, oldest first, with every line the safety net refuses already removed
    /// (see <see cref="DiagnosticsFilter"/>). <paramref name="eventTitles"/> are the event and calendar titles the engine knows now.
    /// </summary>
    IReadOnlyList<string> ReadLastLines(int maxLines, IReadOnlyCollection<string> eventTitles);

    /// <summary>Empties the log. False when a file could not be emptied.</summary>
    bool Clear();
}
