namespace TimeTug.Presentation.Abstractions;

/// <summary>
/// Calls back at a fixed interval so something that Windows gives no change notification for can be looked at again (whether a full-screen app has
/// started or ended, which has no event). A display aid only: it never decides when a takeover happens (the engine does that). The callback runs on a
/// background thread; post to the UI thread from it. Timers live in the platform project by rule.
/// </summary>
public interface IPollTicker
{
    /// <summary>Starts ticking every <paramref name="interval"/>; dispose the result to stop.</summary>
    IDisposable Start(TimeSpan interval, Action tick);
}
