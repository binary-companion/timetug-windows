#if SELFTEST
namespace TimeTug.App.SelfTest;

/// <summary>
/// A clock that stands still until a scenario moves it, in step with the engine's own manual clock (<c>test.advanceClock</c>).
/// The time zone is UTC so a screenshot reads the same on every machine. Timers are the system's.
/// </summary>
internal sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private long _ticks = start.UtcTicks;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

    public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
}
#endif
