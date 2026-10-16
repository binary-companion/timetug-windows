namespace TimeTug.Presentation.Abstractions;

/// <summary>Where the app records what happened. Never given event titles or other calendar content.</summary>
public interface IDiagnostics
{
    /// <summary>Records one entry. <paramref name="level"/> is <c>info</c>, <c>warning</c> or <c>error</c>.</summary>
    void Record(string level, string category, string message);
}
