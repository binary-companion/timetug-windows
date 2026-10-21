namespace TimeTug.Platform.Tests;

/// <summary>
/// A clock whose wall time and monotonic timestamp are separate, as on a real machine: <see cref="Advance"/> moves both, while
/// <see cref="StepWallClock"/> moves only the wall time (the person or a time service changes the clock; no time passes). Code that
/// measures a duration must use the timestamp (<c>GetTimestamp</c>, <c>GetElapsedTime</c>), never a difference of wall times.
/// </summary>
internal sealed class SteppingTime(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _utc = start;
    private long _timestamp = 1_000_000;

    public override long TimestampFrequency => 1000;

    public override DateTimeOffset GetUtcNow() => _utc;

    public override long GetTimestamp() => _timestamp;

    /// <summary>Real time passes: the wall clock and the timestamp both move.</summary>
    public void Advance(TimeSpan by)
    {
        _utc += by;
        _timestamp += (long)by.TotalMilliseconds;
    }

    /// <summary>The wall clock is set forward or back; no time passes.</summary>
    public void StepWallClock(TimeSpan by) => _utc += by;
}
