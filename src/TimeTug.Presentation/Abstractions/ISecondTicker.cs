namespace TimeTug.Presentation.Abstractions;

/// <summary>
/// Calls back once a second so a countdown on screen can redraw. It is a display refresh only: it never decides when a
/// takeover happens (the engine does that). The callback runs on a background thread; post to the UI thread from it.
/// </summary>
public interface ISecondTicker
{
    /// <summary>Starts ticking; dispose the result to stop.</summary>
    IDisposable Start(Action tick);
}
