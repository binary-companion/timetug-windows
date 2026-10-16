namespace TimeTug.Presentation.Abstractions;

/// <summary>
/// Calls back just after every whole minute so wording such as "in 4 min" can refresh. A display refresh only: it never decides
/// when a takeover happens (the engine does that). The callback runs on a background thread.
/// </summary>
public interface IMinuteTicker
{
    /// <summary>Starts ticking; dispose the result to stop.</summary>
    IDisposable Start(Action tick);
}
