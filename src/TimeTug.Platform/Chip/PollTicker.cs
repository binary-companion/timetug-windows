using TimeTug.Presentation.Abstractions;

namespace TimeTug.Platform.Chip;

/// <summary>
/// Ticks at a fixed interval of the time provider's clock, so something Windows gives no change notification for (a full-screen app that starts or ends)
/// can be looked at again. A display aid only; timers live in this project by rule.
/// </summary>
public sealed class PollTicker(TimeProvider time) : IPollTicker
{
    public IDisposable Start(TimeSpan interval, Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        return time.CreateTimer(_ => tick(), null, interval, interval);
    }
}
